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
        using var listener = CreateListener();

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
    public async Task Listener_keeps_retrying_while_the_queue_is_down_and_resumes_when_it_is_back()
    {
        queue.FailNextReceives = 3;
        queue.Enqueue(new QueueMessage("m1", "r1", Upload("after-outage.xlsx")));
        using var listener = CreateListener();

        await listener.StartAsync(CancellationToken.None);
        var message = await channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        Assert.Equal("m1", message.MessageId);
        Assert.True(queue.Receives >= 4, "three failed receives, then the one that returned the message");
        Assert.False(listener.ExecuteTask!.IsCompleted);

        await listener.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.True(listener.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task On_shutdown_the_listener_lets_the_running_receive_finish_and_hands_on_what_it_got()
    {
        var poll = new TaskCompletionSource<IReadOnlyList<QueueMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.PollGate = poll;
        using var listener = CreateListener();
        await listener.StartAsync(CancellationToken.None);
        await queue.PollStarted.Task.WaitAsync(Timeout);

        var stopping = listener.StopAsync(CancellationToken.None);
        Assert.False(stopping.IsCompleted);

        // SQS answers the poll after Ctrl+C: the message must not be lost to a cancelled request.
        poll.SetResult([new QueueMessage("late", "r-late", Upload("late.xlsx"))]);
        await stopping.WaitAsync(Timeout);

        Assert.True(listener.ExecuteTask!.IsCompletedSuccessfully);
        var message = await channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal("late", message.MessageId);
        Assert.True(channel.Reader.Completion.Wait(Timeout));
    }

    [Fact]
    public async Task On_shutdown_the_file_in_progress_and_the_waiting_messages_go_back_to_the_queue()
    {
        importer.BlockOn("large.xlsx");
        using var processor = CreateProcessor(processorCount: 1);

        await processor.StartAsync(CancellationToken.None);
        await channel.Writer.WriteAsync(new FileUploadedMessage("m1", "r1", [new FileUploadedEvent("b", "large.xlsx")]));
        await channel.Writer.WriteAsync(new FileUploadedMessage("m2", "r2", [new FileUploadedEvent("b", "next.xlsx")]));
        await channel.Writer.WriteAsync(new FileUploadedMessage("m3", "r3", [new FileUploadedEvent("b", "last.xlsx")]));
        await importer.WaitUntilBlockedAsync().WaitAsync(Timeout);

        // As on Ctrl+C: the listener completes the channel, then the processors are stopped.
        channel.Writer.Complete();
        await processor.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.True(processor.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(queue.Deleted);
        Assert.Equal(["r1", "r2", "r3"], queue.Released.Order());
        Assert.Empty(importer.Imported);
    }

    [Fact]
    public async Task While_a_file_is_processed_its_message_is_kept_hidden_on_the_queue()
    {
        importer.BlockOn("large.xlsx");
        using var processor = CreateProcessor(processorCount: 1, heartbeat: TimeSpan.FromMilliseconds(20));

        await processor.StartAsync(CancellationToken.None);
        await channel.Writer.WriteAsync(new FileUploadedMessage("m1", "r1", [new FileUploadedEvent("b", "large.xlsx")]));
        await importer.WaitUntilBlockedAsync().WaitAsync(Timeout);
        while (queue.Extended.Count < 3)
        {
            await Task.Delay(10).WaitAsync(Timeout);
        }

        await processor.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.All(queue.Extended, handle => Assert.Equal("r1", handle));
        Assert.Equal(["r1"], queue.Released);
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

    private SqsListener CreateListener() =>
        new(queue, channel, TestPipelines.Create(), NullLogger<SqsListener>.Instance);

    private FileProcessor CreateProcessor(int processorCount, TimeSpan? heartbeat = null)
    {
        var services = new ServiceCollection().AddSingleton<IFileImporter>(importer).BuildServiceProvider();
        return new FileProcessor(
            channel,
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            TestPipelines.Create(),
            Options.Create(new WorkerOptions
            {
                ProcessorCount = processorCount,
                VisibilityHeartbeat = heartbeat ?? TimeSpan.FromMinutes(1),
            }),
            NullLogger<FileProcessor>.Instance);
    }

    private static string Upload(string key) => $$"""
        { "Records": [ { "eventName": "ObjectCreated:Put", "s3": { "bucket": { "name": "b" }, "object": { "key": "{{key}}" } } } ] }
        """;

    /// <summary>Returns the queued batches one per receive; with nothing queued, a short long poll that returns empty.</summary>
    private sealed class FakeQueue : IFileUploadedQueue
    {
        private readonly ConcurrentQueue<QueueMessage[]> batches = new();
        private readonly ConcurrentQueue<string> deleted = new();
        private readonly ConcurrentQueue<string> released = new();
        private readonly ConcurrentQueue<string> extended = new();
        private int receives;

        public IReadOnlyCollection<string> Deleted => deleted.ToArray();

        public IReadOnlyCollection<string> Released => released.ToArray();

        public IReadOnlyCollection<string> Extended => extended.ToArray();

        public int Receives => receives;

        /// <summary>When set, the next receive waits for this batch, like a long poll that gets a message late.</summary>
        public TaskCompletionSource<IReadOnlyList<QueueMessage>>? PollGate { get; set; }

        public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The next receives that fail as if the queue were down.</summary>
        public int FailNextReceives { get; set; }

        public void Enqueue(params QueueMessage[] batch) => batches.Enqueue(batch);

        public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref receives);
            if (FailNextReceives > 0)
            {
                FailNextReceives--;
                throw new QueueException("Could not receive.", new IOException("Connection refused"));
            }

            if (PollGate is { } gate)
            {
                // A long poll in flight: it ends when the test opens the gate, whatever happens to the Worker meanwhile.
                PollGate = null;
                PollStarted.TrySetResult();
                return await gate.Task.WaitAsync(cancellationToken);
            }

            if (batches.TryDequeue(out var batch))
            {
                return batch;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            return [];
        }

        public Task DeleteAsync(string receiptHandle, CancellationToken cancellationToken)
        {
            deleted.Enqueue(receiptHandle);
            return Task.CompletedTask;
        }

        public Task ChangeVisibilityAsync(string receiptHandle, TimeSpan timeout, CancellationToken cancellationToken)
        {
            (timeout == TimeSpan.Zero ? released : extended).Enqueue(receiptHandle);
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

    /// <summary>
    /// Records each imported key; throws like an unreachable storage for keys marked to fail,
    /// and runs until cancelled (a large file) for keys marked to block.
    /// </summary>
    private sealed class FakeImporter : IFileImporter
    {
        private readonly ConcurrentQueue<string> imported = new();
        private readonly ConcurrentDictionary<string, bool> failing = new();
        private readonly ConcurrentDictionary<string, bool> blocking = new();
        private readonly TaskCompletionSource blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<string> Imported => imported.ToArray();

        public void FailOn(string key) => failing[key] = true;

        public void BlockOn(string key) => blocking[key] = true;

        public Task WaitUntilBlockedAsync() => blocked.Task;

        public async Task ImportAsync(FileUploadedEvent upload, CancellationToken cancellationToken)
        {
            if (failing.ContainsKey(upload.Key))
            {
                throw new FileStorageException($"Could not download '{upload.Key}'.", new IOException("down"));
            }

            if (blocking.ContainsKey(upload.Key))
            {
                blocked.TrySetResult();
                await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            }

            imported.Enqueue(upload.Key);
        }
    }
}