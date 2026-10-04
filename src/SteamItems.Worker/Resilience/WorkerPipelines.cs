using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using SteamItems.Worker.Messaging;
using SteamItems.Worker.Storage;

namespace SteamItems.Worker.Resilience;

/// <summary>
/// Polly pipelines for the Worker's outside calls, resolved by key from <see cref="Polly.Registry.ResiliencePipelineProvider{TKey}"/>.
/// Implementations (<see cref="SqsFileUploadedQueue"/>, <see cref="S3FileStorage"/>) make one attempt; callers choose how to retry.
/// A file that still fails after these retries is left on the queue: SQS redelivers it and, after 5 receives, moves it to the DLQ.
/// </summary>
public static class WorkerPipelines
{
    /// <summary>Receiving from SQS. Retries until it works or the Worker stops: 1 s, 2 s, 4 s … up to 30 s between attempts.</summary>
    public const string QueueReceive = "queue-receive";

    /// <summary>Deleting or releasing one message. A few quick retries; if they fail, the message is delivered again.</summary>
    public const string QueueAcknowledge = "queue-acknowledge";

    /// <summary>Downloading a file from S3. Retries transient errors, and a circuit breaker stops calling S3 while it is down.</summary>
    public const string Storage = "storage";

    /// <summary>Writing to worker.db. Retries when SQLite reports the database as busy or locked.</summary>
    public const string Database = "database";

    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    public static IServiceCollection AddWorkerResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline(QueueReceive, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SqsListener));
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<QueueException>(),
                MaxRetryAttempts = int.MaxValue,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Could not receive messages (attempt {Attempt}), retrying in {Delay}",
                        args.AttemptNumber + 1, args.RetryDelay);
                    return default;
                },
            });
        });

        services.AddResiliencePipeline(QueueAcknowledge, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(FileProcessor));
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<QueueException>(),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Queue call failed (attempt {Attempt}), retrying in {Delay}",
                        args.AttemptNumber + 1, args.RetryDelay);
                    return default;
                },
            });
        });

        services.AddResiliencePipeline(Storage, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(S3FileStorage));
            var transient = new PredicateBuilder().Handle<FileStorageException>(ex => ex.IsTransient);

            // Retry is outside the breaker: an open circuit fails at once (BrokenCircuitException) and is not retried.
            builder
                .AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = transient,
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(1),
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            args.Outcome.Exception,
                            "Download failed (attempt {Attempt}), retrying in {Delay}",
                            args.AttemptNumber + 1, args.RetryDelay);
                        return default;
                    },
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    ShouldHandle = transient,
                    FailureRatio = 0.5,
                    MinimumThroughput = 4,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(15),
                    OnOpened = args =>
                    {
                        logger.LogError("S3 looks down: downloads paused for {BreakDuration}", args.BreakDuration);
                        return default;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogInformation("S3 is back: downloads resumed");
                        return default;
                    },
                });
        });

        services.AddResiliencePipeline(Database, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SteamItems.Worker.Data");
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqliteException>(IsBusy)
                    .Handle<DbUpdateException>(ex => ex.InnerException is SqliteException inner && IsBusy(inner)),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "worker.db is busy (attempt {Attempt}), retrying in {Delay}",
                        args.AttemptNumber + 1, args.RetryDelay);
                    return default;
                },
            });
        });

        return services;
    }

    private static bool IsBusy(SqliteException ex) => ex.SqliteErrorCode is SqliteBusy or SqliteLocked;
}
