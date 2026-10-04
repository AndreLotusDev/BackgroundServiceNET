using System.Net;
using System.Text.Json;

namespace SteamItems.Worker.Messaging;

/// <summary>Outcome of parsing an S3 notification body.</summary>
public abstract record S3EventParseResult
{
    private S3EventParseResult() { }

    /// <summary>One or more files were created.</summary>
    public sealed record Created(IReadOnlyList<FileUploadedEvent> Events) : S3EventParseResult;

    /// <summary>A valid message with nothing to process (e.g. the <c>s3:TestEvent</c>). Safe to delete.</summary>
    public sealed record Ignored(string Reason) : S3EventParseResult;

    /// <summary>The body is not an S3 notification we understand.</summary>
    public sealed record Invalid(string Reason) : S3EventParseResult;
}

/// <summary>Turns the JSON body S3 sends to SQS into <see cref="FileUploadedEvent"/>s.</summary>
public static class S3EventParser
{
    private const string TestEvent = "s3:TestEvent";
    private const string ObjectCreatedPrefix = "ObjectCreated:";

    public static S3EventParseResult Parse(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            return new S3EventParseResult.Invalid($"Body is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new S3EventParseResult.Invalid("Body is not a JSON object.");
            }

            // Sent once when the bucket notification is created: {"Service":"Amazon S3","Event":"s3:TestEvent",...}
            if (GetString(root, "Event") == TestEvent)
            {
                return new S3EventParseResult.Ignored(TestEvent);
            }

            if (!root.TryGetProperty("Records", out var records) || records.ValueKind != JsonValueKind.Array)
            {
                return new S3EventParseResult.Invalid("Body has no Records array.");
            }

            var events = new List<FileUploadedEvent>();
            var index = -1;
            foreach (var record in records.EnumerateArray())
            {
                index++;
                var eventName = GetString(record, "eventName");
                if (eventName is null || !eventName.StartsWith(ObjectCreatedPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var bucket = GetString(record, "s3", "bucket", "name");
                var key = GetString(record, "s3", "object", "key");
                if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(key))
                {
                    return new S3EventParseResult.Invalid($"Record {index} has no bucket name or object key.");
                }

                // S3 form-encodes keys in notifications: a space arrives as '+', a literal '+' as '%2B'.
                events.Add(new FileUploadedEvent(bucket, WebUtility.UrlDecode(key)));
            }

            return events.Count > 0
                ? new S3EventParseResult.Created(events)
                : new S3EventParseResult.Ignored("No ObjectCreated records.");
        }
    }

    // Walks nested objects; null when any step is missing or not the expected kind.
    private static string? GetString(JsonElement element, params ReadOnlySpan<string> path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
