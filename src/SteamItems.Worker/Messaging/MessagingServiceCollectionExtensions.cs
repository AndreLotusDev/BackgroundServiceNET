using System.Threading.Channels;

namespace SteamItems.Worker.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFileUploadedQueue"/> as SQS (configured from the <c>Queue</c> section)
    /// and the channel between <see cref="SqsListener"/> and <see cref="FileProcessor"/>.
    /// </summary>
    public static IServiceCollection AddFileUploadedMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SqsQueueOptions>()
            .Bind(configuration.GetSection(SqsQueueOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One client for the app: the SDK client is thread-safe and pools its connections.
        services.AddSingleton<IFileUploadedQueue, SqsFileUploadedQueue>();

        // Unbounded: the listener never waits for the processors. Only the listener writes.
        services.AddSingleton(_ => Channel.CreateUnbounded<FileUploadedMessage>(
            new UnboundedChannelOptions { SingleWriter = true }));

        return services;
    }
}
