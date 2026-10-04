using SteamItems.Worker;
using SteamItems.Worker.Messaging;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddFileUploadedMessaging(builder.Configuration);

// Hosted services stop in reverse order: the listener stops pulling from SQS before the processors stop.
builder.Services.AddHostedService<FileProcessor>();
builder.Services.AddHostedService<SqsListener>();

var host = builder.Build();
host.Run();
