var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.SteamItems_Web>("web")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.SteamItems_Worker>("worker");

builder.Build().Run();
