using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SteamItems.Worker.Messaging;

namespace SteamItems.Worker.Tests;

public sealed class ListenerAndProcessorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeQueue queue = new();
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
    public async Task Processors_delete_each_message_after_processing_it()
    {
        using var processor = new FileProcessor(
            channel, queue, Options.Create(new WorkerOptions { ProcessorCount = 3 }), NullLogger<FileProcessor>.Instance);

        await processor.StartAsync(CancellationToken.None);
        for (var i = 1; i <= 5; i++)
        {
            await channel.Writer.WriteAsync(new FileUploadedMessage($"m{i}", $"r{i}", [new FileUploadedEvent("b", $"{i}.xlsx")]));
        }

        await queue.WaitForDeletesAsync(5).WaitAsync(Timeout);
        Assert.Equal(["r1", "r2", "r3", "r4", "r5"], queue.Deleted.Order());

        await processor.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.True(processor.ExecuteTask!.IsCompletedSuccessfully);
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
}
