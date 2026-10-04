using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SteamItems.Contracts.Status;

namespace SteamItems.Worker.Status;

public static class FileStatusEndpoints
{
    /// <summary>
    /// <c>GET /api/files/status?key=…</c>: the outcome of up to <see cref="FileStatusApi.MaxKeys"/> uploaded files.
    /// Web polls it; Web never reads worker.db itself.
    /// </summary>
    public static IEndpointRouteBuilder MapFileStatus(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(FileStatusApi.Path, async (
                [FromQuery(Name = FileStatusApi.KeyParameter)] string[]? keys,
                FileStatusQuery query,
                CancellationToken cancellationToken) =>
            {
                var distinct = (keys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
                if (distinct.Count is 0 or > FileStatusApi.MaxKeys)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [FileStatusApi.KeyParameter] = [$"Pass between 1 and {FileStatusApi.MaxKeys} object keys."],
                    });
                }

                return Results.Ok(await query.GetAsync(distinct, cancellationToken));
            })
            .WithName("GetFileStatus");

        return endpoints;
    }
}
