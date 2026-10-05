using System.Net;
using System.Text;
using SteamItems.Contracts.Status;
using SteamItems.Web.Status;

namespace SteamItems.Web.Tests.Status;

public sealed class HttpWorkerItemsClientTests
{
    [Fact]
    public async Task Sends_the_key_filter_and_page_and_reads_the_agreed_json()
    {
        var handler = new StubHandler(_ => Json("""
            {"key":"exports/a b.xlsx","fileStatus":"Completed","filter":"Failed","page":2,"pageSize":50,
             "totalCount":51,"processedCount":10,"failedCount":51,"pageCount":2,
             "items":[{"rowNumber":60,"status":"Failed","appId":10,"name":"Game","price":null,"releaseDate":"2020-01-02",
                       "error":"Price must be a number zero or greater, found \"free\".",
                       "raw":{"appId":"10","name":"Game","price":"free","releaseDate":"2020-01-02"}}]}
            """));
        var client = CreateClient(handler);

        var page = await client.GetAsync("exports/a b.xlsx", FileItemStatus.Failed, 2, 50, CancellationToken.None);

        Assert.Equal(
            "http://worker/api/files/items?key=exports%2Fa%20b.xlsx&page=2&pageSize=50&status=Failed",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
        Assert.NotNull(page);
        Assert.Equal((FileProcessingStatus.Completed, FileItemStatus.Failed, 51), (page.FileStatus, page.Filter, page.TotalCount));
        var item = Assert.Single(page.Items);
        Assert.Equal((60, FileItemStatus.Failed, new DateOnly(2020, 1, 2)), (item.RowNumber, item.Status, item.ReleaseDate));
        Assert.Equal("free", item.Raw?.Price);
    }

    [Fact]
    public async Task Without_a_filter_no_status_is_sent()
    {
        var handler = new StubHandler(_ => Json("""
            {"key":"k","fileStatus":"Completed","filter":null,"page":1,"pageSize":50,
             "totalCount":0,"processedCount":0,"failedCount":0,"items":[]}
            """));

        await CreateClient(handler).GetAsync("k", null, 1, 50, CancellationToken.None);

        Assert.DoesNotContain("status=", Assert.Single(handler.Requests).RequestUri!.Query);
    }

    [Fact]
    public async Task An_unknown_key_is_null()
    {
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await client.GetAsync("k", null, 1, 50, CancellationToken.None));
    }

    [Fact]
    public async Task An_error_answer_means_the_worker_is_unavailable()
    {
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<WorkerUnavailableException>(() => client.GetAsync("k", null, 1, 50, CancellationToken.None));
    }

    [Fact]
    public async Task A_refused_connection_means_the_worker_is_unavailable()
    {
        var client = CreateClient(new StubHandler(_ => throw new HttpRequestException("Connection refused")));

        var ex = await Assert.ThrowsAsync<WorkerUnavailableException>(
            () => client.GetAsync("k", null, 1, 50, CancellationToken.None));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    private static HttpWorkerItemsClient CreateClient(StubHandler handler) =>
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
