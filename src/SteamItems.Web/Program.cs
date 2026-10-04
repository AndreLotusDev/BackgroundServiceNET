using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using SteamItems.Web;
using SteamItems.Web.Data;
using SteamItems.Web.Export;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddControllersWithViews();

// web.db: Steam items catalog (seeded by the migration) and each user's selection.
builder.Services.AddDbContext<WebDbContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("WebDb")
        ?? throw new InvalidOperationException("Connection string 'WebDb' not found.")));

// Excel export of the saved selection.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IItemsExcelWriter, ItemsExcelWriter>();
builder.Services.AddScoped<SelectionExporter>();

// Cookie session for the MVC pages; anonymous users are challenged through SteamItems.Identity.
var identity = builder.Configuration.GetSection("Identity");
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = identity["Authority"];
        options.ClientId = identity["ClientId"];
        options.ClientSecret = identity["ClientSecret"];
        options.ResponseType = "code";
        options.UsePkce = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("api");
        options.Scope.Add("offline_access");

        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "role";
    })
    // Bearer tokens for /api (Swagger UI and other API clients).
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Authority = identity["Authority"];
        options.MapInboundClaims = false;
        // IdentityServer only defines the "api" scope (no ApiResource), so tokens carry no audience;
        // the ApiScope policy checks the scope claim instead.
        options.TokenValidationParameters.ValidateAudience = false;
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AuthPolicies.ApiScope, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireClaim("scope", "api")));

// Swagger UI signs in against SteamItems.Identity with the public "swagger" client (code + PKCE).
var authority = identity["Authority"]?.TrimEnd('/');
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SteamItems API", Version = "v1" });

    // Only /api controllers belong in the document, not the MVC pages.
    options.DocInclusionPredicate((_, api) => api.RelativePath?.StartsWith("api/") == true);

    options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"{authority}/connect/authorize"),
                TokenUrl = new Uri($"{authority}/connect/token"),
                Scopes = new Dictionary<string, string> { ["api"] = "SteamItems API" },
            },
        },
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("oauth2", document)] = ["api"],
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<WebDbContext>().Database.MigrateAsync();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.OAuthClientId(identity["SwaggerClientId"]);
    options.OAuthScopes("api");
    options.OAuthUsePkce();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.MapControllers();

app.MapDefaultEndpoints();

app.Run();
