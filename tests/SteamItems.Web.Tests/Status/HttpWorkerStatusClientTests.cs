using System.Net;
using System.Text;
using SteamItems.Contracts.Status;
using SteamItems.Web.Status;

namespace SteamItems.Web.Tests.Status;

public sealed class HttpWorkerStatusClientTests
{
    [Fact]
    public async Task Sends_every_key_in_the_query_string_and_reads_the_agreed_json()
    {
        var handler = new StubHandler(_ => Json("""
            [{"key":"exports/alice/1.xlsx","status":"Completed","processedCount":2,"failedCount":1,
              "error":null,"startedAt":"2026-10-04T12:00:00+00:00","completedAt":"2026-10-04T12:01:00+00:00"}]
            """));
        var client = CreateClient(handler);

        var statuses = await client.GetAsync(["exports/alice/1.xlsx", "exports/a b+c.xlsx"], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "http://worker/api/files/status?key=exports%2Falice%2F1.xlsx&key=exports%2Fa%20b%2Bc.xlsx",
            request.RequestUri!.AbsoluteUri);

        var status = Assert.Single(statuses);
        Assert.Equal(FileProcessingStatus.Completed, status.Status);
        Assert.Equal((2, 1), (status.ProcessedCount, status.FailedCount));
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 12, 1, 0, TimeSpan.Zero), status.CompletedAt);
    }

    [Fact]
    public async Task An_error_answer_means_the_worker_is_unavailable()
    {
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<WorkerUnavailableException>(() => client.GetAsync(["k"], CancellationToken.None));
    }

    [Fact]
    public async Task A_refused_connection_means_the_worker_is_unavailable()
    {
        var client = CreateClient(new StubHandler(_ => throw new HttpRequestException("Connection refused")));

        var ex = await Assert.ThrowsAsync<WorkerUnavailableException>(() => client.GetAsync(["k"], CancellationToken.None));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task A_body_that_is_not_the_agreed_json_means_the_worker_is_unavailable()
    {
        var client = CreateClient(new StubHandler(_ => Json("<html>not the worker</html>")));

        await Assert.ThrowsAsync<WorkerUnavailableException>(() => client.GetAsync(["k"], CancellationToken.None));
    }

    private static HttpWorkerStatusClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://worker") });

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
