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

    /// <summary>
    /// <c>GET /api/files/items?key=…&amp;status=…&amp;page=…&amp;pageSize=…</c>: one page of the rows stored for an uploaded file.
    /// Web calls it for the export details page; Web never reads worker.db itself.
    /// </summary>
    public static IEndpointRouteBuilder MapFileItems(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(FileItemsApi.Path, async (
                [FromQuery(Name = FileItemsApi.KeyParameter)] string? key,
                [FromQuery(Name = FileItemsApi.StatusParameter)] string? status,
                [FromQuery(Name = FileItemsApi.PageParameter)] int? page,
                [FromQuery(Name = FileItemsApi.PageSizeParameter)] int? pageSize,
                FileItemsQuery query,
                CancellationToken cancellationToken) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (string.IsNullOrWhiteSpace(key))
                {
                    errors[FileItemsApi.KeyParameter] = ["Pass the object key of the file."];
                }

                FileItemStatus? filter = null;
                if (!string.IsNullOrEmpty(status))
                {
                    if (Enum.TryParse<FileItemStatus>(status, ignoreCase: true, out var parsed)
                        && Enum.IsDefined(parsed))
                    {
                        filter = parsed;
                    }
                    else
                    {
                        errors[FileItemsApi.StatusParameter] =
                            [$"Leave it out for every row, or pass one of: {string.Join(", ", Enum.GetNames<FileItemStatus>())}."];
                    }
                }

                if (page is < 1)
                {
                    errors[FileItemsApi.PageParameter] = ["Pages start at 1."];
                }

                if (pageSize is < 1 or > FileItemsApi.MaxPageSize)
                {
                    errors[FileItemsApi.PageSizeParameter] = [$"Pass between 1 and {FileItemsApi.MaxPageSize}."];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var items = await query.GetAsync(
                    key!, filter, page ?? 1, pageSize ?? FileItemsApi.DefaultPageSize, cancellationToken);
                return items is null
                    ? Results.Problem($"No file has been processed for key '{key}'.", statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok(items);
            })
            .WithName("GetFileItems");

        return endpoints;
    }
}
