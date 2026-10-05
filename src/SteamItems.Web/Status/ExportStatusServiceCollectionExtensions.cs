using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace SteamItems.Web.Status;

public static class ExportStatusServiceCollectionExtensions
{
    /// <summary>
    /// Registers the export status flow: the Worker status client (configured from the <c>WorkerStatus</c> section),
    /// the poller, the reader of an export's rows and the SignalR hub that pushes changes to the browser. Map the hub with <see cref="ExportsHub.Path"/>.
    /// </summary>
    public static IServiceCollection AddExportStatus(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkerStatusOptions>()
            .Bind(configuration.GetSection(WorkerStatusOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IWorkerStatusClient, HttpWorkerStatusClient>((provider, http) =>
            http.BaseAddress = provider.GetRequiredService<IOptions<WorkerStatusOptions>>().Value.BaseUrl);

        // The rows of one export for its details page, from the same Worker.
        services.AddHttpClient<IWorkerItemsClient, HttpWorkerItemsClient>((provider, http) =>
            http.BaseAddress = provider.GetRequiredService<IOptions<WorkerStatusOptions>>().Value.BaseUrl);
        services.AddScoped<ExportItemsReader>();

        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
        services.AddSingleton<IExportStatusNotifier, SignalRExportStatusNotifier>();

        services.AddScoped<ExportStatusUpdater>();
        services.AddHostedService<ExportStatusPoller>();
        return services;
    }
}
