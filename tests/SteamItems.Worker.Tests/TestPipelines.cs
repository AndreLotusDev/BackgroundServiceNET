using Polly;
using Polly.Registry;
using SteamItems.Worker.Messaging;
using SteamItems.Worker.Resilience;
using SteamItems.Worker.Storage;

namespace SteamItems.Worker.Tests;

/// <summary>The Worker's retry rules without the waits (and without the circuit breaker), so tests run fast.</summary>
internal static class TestPipelines
{
    public const int StorageRetries = 3;

    public static ResiliencePipelineProvider<string> Create()
    {
        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder(WorkerPipelines.QueueReceive, (builder, _) => builder.AddRetry(new()
        {
            ShouldHandle = new PredicateBuilder().Handle<QueueException>(),
            MaxRetryAttempts = int.MaxValue,
            Delay = TimeSpan.Zero,
        }));
        registry.TryAddBuilder(WorkerPipelines.QueueAcknowledge, (builder, _) => builder.AddRetry(new()
        {
            ShouldHandle = new PredicateBuilder().Handle<QueueException>(),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.Zero,
        }));
        registry.TryAddBuilder(WorkerPipelines.Storage, (builder, _) => builder.AddRetry(new()
        {
            ShouldHandle = new PredicateBuilder().Handle<FileStorageException>(ex => ex.IsTransient),
            MaxRetryAttempts = StorageRetries,
            Delay = TimeSpan.Zero,
        }));
        registry.TryAddBuilder(WorkerPipelines.Database, (_, _) => { });
        return registry;
    }
}
