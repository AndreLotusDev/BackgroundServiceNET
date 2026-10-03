var builder = DistributedApplication.CreateBuilder(args);

// The OIDC flow needs HTTPS on both sides, so both projects run their "https" launch profile.
var identity = builder.AddProject<Projects.SteamItems_Identity>("identity", launchProfileName: "https")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.SteamItems_Web>("web", launchProfileName: "https")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Identity__Authority", identity.GetEndpoint("https"))
    .WaitFor(identity);

builder.AddProject<Projects.SteamItems_Worker>("worker");

builder.Build().Run();
