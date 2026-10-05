using System.Net;
using System.Text.Json;
using Polly;
using SteamItems.Contracts.Status;

namespace SteamItems.Web.Status;

/// <summary>The Worker's rows endpoint. Web reads the stored rows only through this, never from worker.db.</summary>
public interface IWorkerItemsClient
{
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">At most <see cref="FileItemsApi.MaxPageSize"/>.</param>
    /// <returns>Null when the Worker has no file for the key.</returns>
    /// <exception cref="WorkerUnavailableException">The Worker is down or did not answer properly.</exception>
    Task<FileItemsPage?> GetAsync(
        string key, FileItemStatus? filter, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IWorkerItemsClient"/> over HTTP, with the same base address and resilience handler as
/// <see cref="HttpWorkerStatusClient"/>.
/// </summary>
public sealed class HttpWorkerItemsClient(HttpClient http) : IWorkerItemsClient
{
    public async Task<FileItemsPage?> GetAsync(
        string key, FileItemStatus? filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = $"{FileItemsApi.KeyParameter}={Uri.EscapeDataString(key)}"
            + $"&{FileItemsApi.PageParameter}={page}&{FileItemsApi.PageSizeParameter}={pageSize}";
        if (filter is { } status)
        {
            query += $"&{FileItemsApi.StatusParameter}={status}";
        }

        try
        {
            using var response = await http.GetAsync($"{FileItemsApi.Path}?{query}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new WorkerUnavailableException($"The Worker answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return await response.Content.ReadFromJsonAsync<FileItemsPage>(cancellationToken)
                ?? throw new WorkerUnavailableException("The Worker answered with an empty body.");
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw new WorkerUnavailableException($"Could not get the rows of '{key}' from the Worker at {http.BaseAddress}.", ex);
        }
    }

    // Same rule as HttpWorkerStatusClient: connection, timeout, open circuit or bad JSON. Caller cancellation is not a failure.
    private static bool IsUnavailable(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        HttpRequestException or ExecutionRejectedException or JsonException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };
}
