using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SteamItems.Identity;
using SteamItems.Identity.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("Connection string 'IdentityDb' not found.");
var migrationsAssembly = typeof(Program).Assembly.GetName().Name;

builder.Services.AddControllersWithViews();

// ASP.NET Identity: users, passwords, roles (AspNet* tables).
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// IdentityServer: clients/scopes in memory (Config.cs), grants and signing keys in SQLite.
builder.Services.AddIdentityServer(options =>
    {
        options.Events.RaiseErrorEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseSuccessEvents = true;
    })
    .AddInMemoryIdentityResources(Config.IdentityResources)
    .AddInMemoryApiScopes(Config.ApiScopes)
    .AddInMemoryClients(Config.Clients(builder.Configuration))
    .AddOperationalStore(options =>
        options.ConfigureDbContext = db => db.UseSqlite(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly)))
    .AddAspNetIdentity<IdentityUser>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await SeedData.InitializeAsync(app.Services);
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseIdentityServer();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapDefaultEndpoints();

app.Run();
