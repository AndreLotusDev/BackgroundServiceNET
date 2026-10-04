using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Polly;
using SteamItems.Contracts.Status;

namespace SteamItems.Web.Status;

/// <summary>The <c>WorkerStatus</c> configuration section.</summary>
public sealed class WorkerStatusOptions
{
    public const string SectionName = "WorkerStatus";

    /// <summary>Where the Worker serves <see cref="FileStatusApi.Path"/>.</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>How often unfinished exports are checked. No request is made while every export is finished.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>The Worker's status endpoint. Web reads the Worker's outcome only through this, never from worker.db.</summary>
public interface IWorkerStatusClient
{
    /// <param name="keys">At most <see cref="FileStatusApi.MaxKeys"/> object keys.</param>
    /// <returns>One entry per key the Worker has seen; unknown keys are left out.</returns>
    /// <exception cref="WorkerUnavailableException">The Worker is down or did not answer properly.</exception>
    Task<IReadOnlyList<FileStatusResponse>> GetAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}

/// <summary>The Worker's status endpoint could not be reached or gave an unusable answer.</summary>
public sealed class WorkerUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// <see cref="IWorkerStatusClient"/> over HTTP. The client comes from <c>IHttpClientFactory</c>, so the service defaults'
/// standard resilience handler (retries, timeouts, circuit breaker) is already around each call.
/// </summary>
public sealed class HttpWorkerStatusClient(HttpClient http) : IWorkerStatusClient
{
    public async Task<IReadOnlyList<FileStatusResponse>> GetAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var query = string.Join("&", keys.Select(k => $"{FileStatusApi.KeyParameter}={Uri.EscapeDataString(k)}"));
        try
        {
            using var response = await http.GetAsync($"{FileStatusApi.Path}?{query}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new WorkerUnavailableException($"The Worker answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return await response.Content.ReadFromJsonAsync<List<FileStatusResponse>>(cancellationToken)
                ?? throw new WorkerUnavailableException("The Worker answered with an empty body.");
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw new WorkerUnavailableException($"Could not get file status from the Worker at {http.BaseAddress}.", ex);
        }
    }

    // Connection refused, timeout, open circuit, or a body that is not the agreed JSON. Caller cancellation is not a failure.
    private static bool IsUnavailable(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        HttpRequestException or ExecutionRejectedException or JsonException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };
}
