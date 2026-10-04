var builder = DistributedApplication.CreateBuilder(args);

// The OIDC flow needs HTTPS on both sides, so both projects run their "https" launch profile.
var identity = builder.AddProject<Projects.SteamItems_Identity>("identity", launchProfileName: "https")
    .WithExternalHttpEndpoints();

// Its HTTP endpoint serves the file status that Web polls.
var worker = builder.AddProject<Projects.SteamItems_Worker>("worker");

// No WaitFor(worker): Web works without the Worker, exports just stay Pending until it is up.
builder.AddProject<Projects.SteamItems_Web>("web", launchProfileName: "https")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Identity__Authority", identity.GetEndpoint("https"))
    .WithEnvironment("WorkerStatus__BaseUrl", worker.GetEndpoint("http"))
    .WaitFor(identity);

builder.Build().Run();
