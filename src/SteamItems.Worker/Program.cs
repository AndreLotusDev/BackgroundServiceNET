using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using SteamItems.Worker;
using SteamItems.Worker.Data;
using SteamItems.Worker.Import;
using SteamItems.Worker.Messaging;
using SteamItems.Worker.Resilience;
using SteamItems.Worker.Status;
using SteamItems.Worker.Storage;

// A web host (not only a generic host) because the Worker also serves the status endpoint that Web polls.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// ShutdownTimeout: how long Ctrl+C / SIGTERM waits for the processors to stop after their current row.
// BackgroundServiceExceptionBehavior: StopHost. The loops catch the failures they expect (queue, S3, SQLite),
// so an exception that still escapes ExecuteAsync is a bug and stops the process instead of leaving it half-alive.
builder.Services.Configure<HostOptions>(builder.Configuration.GetSection("HostOptions"));

builder.Services.AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddFileUploadedMessaging(builder.Configuration);

// worker.db: the rows read from the uploaded files. The Worker has no access to web.db.
builder.Services.AddDbContext<WorkerDbContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("WorkerDb")
        ?? throw new InvalidOperationException("Connection string 'WorkerDb' not found.")));

// Download from S3 (LocalStack in Development) and import row by row.
builder.Services.AddS3FileStorage(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IFileImporter, FileImporter>();

// Outcome of each file for Web (task 11).
builder.Services.AddScoped<FileStatusQuery>();

// Rows of each file for the export details page (task 12).
builder.Services.AddScoped<FileItemsQuery>();

// Retry, backoff and circuit breaker for SQS, S3 and SQLite.
builder.Services.AddWorkerResilience();

// Hosted services stop in reverse order: the listener stops pulling from SQS before the processors stop.
builder.Services.AddHostedService<FileProcessor>();
builder.Services.AddHostedService<SqsListener>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>().Database;
    await db.MigrateAsync();

    // EF creates the file in WAL mode. The sqlite-web container (task 14) opens it across the
    // host/VM boundary, where SQLite locks and the -shm memory map are not shared; in WAL mode even
    // a read-only reader writes to -shm. Rollback-journal mode keeps that reader off our files.
    // Persisted in the file, so this only changes something the first time.
    await db.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");
}

app.MapFileStatus();
app.MapFileItems();
app.MapDefaultEndpoints();

app.Run();
