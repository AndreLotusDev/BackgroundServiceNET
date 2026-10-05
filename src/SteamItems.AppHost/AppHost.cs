using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// Local AWS (S3 + SQS). Same setup as docker-compose.yml; fixed port 4566 so the
// projects' "ServiceUrl": "http://localhost:4566" settings keep working.
// Pinned: since 2026-03-23 the `latest` tag requires LOCALSTACK_AUTH_TOKEN.
var localstack = builder.AddContainer("localstack", "localstack/localstack", "4.12")
    .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "http")
    .WithEnvironment("SERVICES", "s3,sqs")
    .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
    .WithBindMount("../../localstack/init/ready.d", "/etc/localstack/init/ready.d", isReadOnly: true)
    .WithHealthCheck("localstack-ready");

// Healthy only after the ready.d scripts have finished (bucket, queues, notification exist).
var localstackHttp = localstack.GetEndpoint("http");
var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
builder.Services.AddHealthChecks().AddAsyncCheck("localstack-ready", async ct =>
{
    try
    {
        var body = await healthClient.GetStringAsync($"{localstackHttp.Url}/_localstack/init/ready", ct);
        return body.Contains("\"completed\": true")
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("init scripts still running");
    }
    catch (Exception ex)
    {
        return HealthCheckResult.Unhealthy("LocalStack not reachable", ex);
    }
});

// S3 browser on http://localhost:8080.
builder.AddContainer("s3manager", "cloudlena/s3manager", "latest")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithEnvironment("ENDPOINT", localstackHttp.Property(EndpointProperty.HostAndPort))
    .WithEnvironment("REGION", "us-east-1")
    .WithEnvironment("ACCESS_KEY_ID", "test")
    .WithEnvironment("SECRET_ACCESS_KEY", "test")
    .WithEnvironment("USE_SSL", "false")
    .WithEnvironment("BUCKET_LOOKUP", "Path")
    .WithEnvironment("LIST_RECURSIVE", "true")
    .WaitFor(localstack);

// The OIDC flow needs HTTPS on both sides, so both projects run their "https" launch profile.
var identity = builder.AddProject<Projects.SteamItems_Identity>("identity", launchProfileName: "https")
    .WithExternalHttpEndpoints();

// Its HTTP endpoint serves the file status that Web polls.
var worker = builder.AddProject<Projects.SteamItems_Worker>("worker")
    .WaitFor(localstack);

// No WaitFor(worker): Web works without the Worker, exports just stay Pending until it is up.
builder.AddProject<Projects.SteamItems_Web>("web", launchProfileName: "https")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Identity__Authority", identity.GetEndpoint("https"))
    .WithEnvironment("WorkerStatus__BaseUrl", worker.GetEndpoint("http"))
    .WaitFor(identity)
    .WaitFor(localstack);

builder.Build().Run();
