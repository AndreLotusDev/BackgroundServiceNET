namespace SteamItems.Worker.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IFileStorage"/> as S3, configured from the <c>FileStorage</c> section.</summary>
    public static IServiceCollection AddS3FileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<S3StorageOptions>()
            .Bind(configuration.GetSection(S3StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One client for the app: the SDK client is thread-safe and pools its connections.
        services.AddSingleton<IFileStorage, S3FileStorage>();
        return services;
    }
}
