using Duende.IdentityServer.EntityFramework.DbContexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SteamItems.Identity.Data;

public sealed class SeedUser
{
    public required string UserName { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public static class SeedData
{
    /// <summary>
    /// Applies pending migrations for both contexts and creates the users listed under
    /// the "SeedUsers" configuration section if they don't exist yet.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILogger<ApplicationDbContext>>();

        await provider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<PersistedGrantDbContext>().Database.MigrateAsync();

        var seedUsers = provider.GetRequiredService<IConfiguration>()
            .GetSection("SeedUsers").Get<List<SeedUser>>() ?? [];
        var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();

        foreach (var seed in seedUsers)
        {
            if (await userManager.FindByNameAsync(seed.UserName) is not null)
            {
                continue;
            }

            var user = new IdentityUser { UserName = seed.UserName, Email = seed.Email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, seed.Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed user '{seed.UserName}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }

            logger.LogInformation("Seeded user {UserName}", seed.UserName);
        }
    }
}
