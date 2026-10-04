using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SteamItems.Worker.Import;
using SteamItems.Worker.Messaging;
using SteamItems.Worker.Storage;

namespace SteamItems.Worker.Tests;

public sealed class ListenerAndProcessorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeQueue queue = new();
    private readonly FakeImporter importer = new();
    private readonly Channel<FileUploadedMessage> channel = Channel.CreateUnbounded<FileUploadedMessage>();

    [Fact]
    public async Task Listener_queues_uploads_deletes_test_events_and_survives_malformed_messages()
    {
        queue.Enqueue(
            new QueueMessage("m1", "r1", Upload("first.xlsx")),
            new QueueMessage("m2", "r2", """{"Service":"Amazon S3","Event":"s3:TestEvent"}"""),
            new QueueMessage("m3", "r3", "garbage"));
        queue.Enqueue(new QueueMessage("m4", "r4", Upload("second.xlsx")));
        using var listener = new SqsListener(queue, channel, NullLogger<SqsListener>.Instance);

        await listener.StartAsync(CancellationToken.None);
        var first = await channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        var second = await channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        // The malformed message did not stop the loop: the next batch was still read.
        Assert.Equal(("m1", "r1", "first.xlsx"), (first.MessageId, first.ReceiptHandle, Assert.Single(first.Events).Key));
        Assert.Equal("m4", second.MessageId);
        Assert.False(listener.ExecuteTask!.IsCompleted);

        // Only the test event is deleted; uploads wait for the processor, the malformed one is left for the DLQ.
        Assert.Equal(["r2"], queue.Deleted);

        await listener.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.True(listener.ExecuteTask.IsCompletedSuccessfully);
        Assert.True(channel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task Processors_delete_each_message_after_importing_it()
    {
        using var processor = CreateProcessor(processorCount: 3);

        await processor.StartAsync(CancellationToken.None);
        for (var i = 1; i <= 5; i++)
        {
            await channel.Writer.WriteAsync(new FileUploadedMessage($"m{i}", $"r{i}", [new FileUploadedEvent("b", $"{i}.xlsx")]));
        }

        await queue.WaitForDeletesAsync(5).WaitAsync(Timeout);
        Assert.Equal(["r1", "r2", "r3", "r4", "r5"], queue.Deleted.Order());
        Assert.Equal(["1.xlsx", "2.xlsx", "3.xlsx", "4.xlsx", "5.xlsx"], importer.Imported.Order());

        await processor.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.True(processor.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_failed_import_leaves_the_message_on_the_queue_and_the_processor_keeps_going()
    {
        importer.FailOn("bad.xlsx");
        using var processor = CreateProcessor(processorCount: 1);

        await processor.StartAsync(CancellationToken.None);
        await channel.Writer.WriteAsync(new FileUploadedMessage("m1", "r1", [new FileUploadedEvent("b", "bad.xlsx")]));
        await channel.Writer.WriteAsync(new FileUploadedMessage("m2", "r2", [new FileUploadedEvent("b", "good.xlsx")]));

        await queue.WaitForDeletesAsync(1).WaitAsync(Timeout);
        Assert.Equal(["r2"], queue.Deleted);

        await processor.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.True(processor.ExecuteTask!.IsCompletedSuccessfully);
    }

    private FileProcessor CreateProcessor(int processorCount)
    {
        var services = new ServiceCollection().AddSingleton<IFileImporter>(importer).BuildServiceProvider();
        return new FileProcessor(
            channel,
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WorkerOptions { ProcessorCount = processorCount }),
            NullLogger<FileProcessor>.Instance);
    }

    private static string Upload(string key) => $$"""
        { "Records": [ { "eventName": "ObjectCreated:Put", "s3": { "bucket": { "name": "b" }, "object": { "key": "{{key}}" } } } ] }
        """;

    /// <summary>Returns the queued batches one per receive, then long-polls until cancelled.</summary>
    private sealed class FakeQueue : IFileUploadedQueue
    {
        private readonly ConcurrentQueue<QueueMessage[]> batches = new();
        private readonly ConcurrentQueue<string> deleted = new();

        public IReadOnlyCollection<string> Deleted => deleted.ToArray();

        public void Enqueue(params QueueMessage[] batch) => batches.Enqueue(batch);

        public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (batches.TryDequeue(out var batch))
            {
                return batch;
            }

            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return [];
        }

        public Task DeleteAsync(string receiptHandle, CancellationToken cancellationToken)
        {
            deleted.Enqueue(receiptHandle);
            return Task.CompletedTask;
        }

        public async Task WaitForDeletesAsync(int count)
        {
            while (deleted.Count < count)
            {
                await Task.Delay(10);
            }
        }
    }

    /// <summary>Records each imported key; throws like an unreachable storage for keys marked to fail.</summary>
    private sealed class FakeImporter : IFileImporter
    {
        private readonly ConcurrentQueue<string> imported = new();
        private readonly ConcurrentDictionary<string, bool> failing = new();

        public IReadOnlyCollection<string> Imported => imported.ToArray();

        public void FailOn(string key) => failing[key] = true;

        public Task ImportAsync(FileUploadedEvent upload, CancellationToken cancellationToken)
        {
            if (failing.ContainsKey(upload.Key))
            {
                throw new FileStorageException($"Could not download '{upload.Key}'.", new IOException("down"));
            }

            imported.Enqueue(upload.Key);
            return Task.CompletedTask;
        }
    }
}