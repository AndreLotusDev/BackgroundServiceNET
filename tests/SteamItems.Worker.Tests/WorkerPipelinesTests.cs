using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Registry;
using SteamItems.Worker.Resilience;
using SteamItems.Worker.Storage;

namespace SteamItems.Worker.Tests;

/// <summary>The pipelines the Worker registers, with their real settings.</summary>
public sealed class WorkerPipelinesTests
{
    private readonly ResiliencePipelineProvider<string> pipelines = new ServiceCollection()
        .AddLogging()
        .AddWorkerResilience()
        .BuildServiceProvider()
        .GetRequiredService<ResiliencePipelineProvider<string>>();

    [Theory]
    [InlineData(WorkerPipelines.QueueReceive)]
    [InlineData(WorkerPipelines.QueueAcknowledge)]
    [InlineData(WorkerPipelines.Storage)]
    [InlineData(WorkerPipelines.Database)]
    public void Every_pipeline_is_registered(string key) =>
        Assert.NotNull(pipelines.GetPipeline(key));

    [Fact]
    public async Task Storage_does_not_retry_a_permanent_error()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<FileStorageException>(async () =>
            await pipelines.GetPipeline(WorkerPipelines.Storage).ExecuteAsync(_ =>
            {
                attempts++;
                throw new FileStorageException("missing", new FileNotFoundException(), isTransient: false);
            }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Storage_opens_the_circuit_when_S3_keeps_failing()
    {
        var storage = pipelines.GetPipeline(WorkerPipelines.Storage);
        var attempts = 0;
        ValueTask Down(CancellationToken _)
        {
            attempts++;
            throw new FileStorageException("down", new IOException("Connection refused"));
        }

        // The first file: 1 try + 3 retries, 4 failures in a row, which opens the circuit.
        await Assert.ThrowsAnyAsync<Exception>(async () => await storage.ExecuteAsync(Down));
        var attemptsBefore = attempts;

        // The next file fails at once without calling S3.
        await Assert.ThrowsAsync<BrokenCircuitException>(async () => await storage.ExecuteAsync(Down));
        Assert.Equal(attemptsBefore, attempts);
    }
}
