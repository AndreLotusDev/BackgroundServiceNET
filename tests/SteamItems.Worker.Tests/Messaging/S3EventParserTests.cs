using SteamItems.Worker.Messaging;

namespace SteamItems.Worker.Tests.Messaging;

public sealed class S3EventParserTests
{
    // Shape of the body S3 (and LocalStack) sends to SQS for an upload, trimmed to the fields that matter plus a few neighbours.
    private const string PutEvent = """
        {
          "Records": [
            {
              "eventVersion": "2.1",
              "eventSource": "aws:s3",
              "awsRegion": "us-east-1",
              "eventTime": "2026-10-04T12:30:00.000Z",
              "eventName": "ObjectCreated:Put",
              "s3": {
                "s3SchemaVersion": "1.0",
                "configurationId": "notification-1",
                "bucket": { "name": "steam-items-uploads", "arn": "arn:aws:s3:::steam-items-uploads" },
                "object": { "key": "exports/alice/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b.xlsx", "size": 6144, "eTag": "\"abc123\"" }
              }
            }
          ]
        }
        """;

    [Fact]
    public void Put_event_becomes_a_file_uploaded_event()
    {
        var result = S3EventParser.Parse(PutEvent);

        var created = Assert.IsType<S3EventParseResult.Created>(result);
        var file = Assert.Single(created.Events);
        Assert.Equal("steam-items-uploads", file.Bucket);
        Assert.Equal("exports/alice/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b.xlsx", file.Key);
    }

    [Fact]
    public void Key_is_url_decoded()
    {
        var result = S3EventParser.Parse(Event("ObjectCreated:Put", "steam-items-uploads", "exports/my+file%2B1%C3%A9.xlsx"));

        var created = Assert.IsType<S3EventParseResult.Created>(result);
        Assert.Equal("exports/my file+1é.xlsx", Assert.Single(created.Events).Key);
    }

    [Fact]
    public void Every_object_created_record_is_returned()
    {
        var body = $$"""
            { "Records": [ {{Record("ObjectCreated:Put", "b", "one.xlsx")}}, {{Record("ObjectRemoved:Delete", "b", "gone.xlsx")}}, {{Record("ObjectCreated:CompleteMultipartUpload", "b", "two.xlsx")}} ] }
            """;

        var created = Assert.IsType<S3EventParseResult.Created>(S3EventParser.Parse(body));

        Assert.Equal([new FileUploadedEvent("b", "one.xlsx"), new FileUploadedEvent("b", "two.xlsx")], created.Events);
    }

    [Fact]
    public void Test_event_is_ignored()
    {
        const string body = """
            {"Service":"Amazon S3","Event":"s3:TestEvent","Time":"2026-10-04T12:00:00.000Z","Bucket":"steam-items-uploads","RequestId":"1","HostId":"2"}
            """;

        var ignored = Assert.IsType<S3EventParseResult.Ignored>(S3EventParser.Parse(body));
        Assert.Equal("s3:TestEvent", ignored.Reason);
    }

    [Fact]
    public void Records_without_object_created_are_ignored()
    {
        var body = $$"""{ "Records": [ {{Record("ObjectRemoved:Delete", "b", "gone.xlsx")}} ] }""";

        Assert.IsType<S3EventParseResult.Ignored>(S3EventParser.Parse(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Records\": [")]
    [InlineData("[1, 2]")]
    [InlineData("\"text\"")]
    [InlineData("{}")]
    [InlineData("{\"Records\": {}}")]
    public void Body_that_is_not_an_s3_notification_is_invalid(string body)
    {
        Assert.IsType<S3EventParseResult.Invalid>(S3EventParser.Parse(body));
    }

    [Theory]
    [InlineData("""{ "Records": [ { "eventName": "ObjectCreated:Put" } ] }""")]
    [InlineData("""{ "Records": [ { "eventName": "ObjectCreated:Put", "s3": { "bucket": { "name": "b" }, "object": {} } } ] }""")]
    [InlineData("""{ "Records": [ { "eventName": "ObjectCreated:Put", "s3": { "bucket": { "name": "" }, "object": { "key": "k" } } } ] }""")]
    [InlineData("""{ "Records": [ { "eventName": "ObjectCreated:Put", "s3": { "bucket": "b", "object": { "key": 5 } } } ] }""")]
    public void Object_created_record_without_bucket_or_key_is_invalid(string body)
    {
        var invalid = Assert.IsType<S3EventParseResult.Invalid>(S3EventParser.Parse(body));
        Assert.Contains("Record 0", invalid.Reason);
    }

    private static string Event(string eventName, string bucket, string key) =>
        $$"""{ "Records": [ {{Record(eventName, bucket, key)}} ] }""";

    private static string Record(string eventName, string bucket, string key) =>
        $$"""{ "eventName": "{{eventName}}", "s3": { "bucket": { "name": "{{bucket}}" }, "object": { "key": "{{key}}" } } }""";
}
