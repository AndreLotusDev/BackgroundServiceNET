namespace SteamItems.Web.Data;

/// <summary>Static catalog for the sample. App ids and release dates are real; prices are sample values in USD.</summary>
public static class SteamItemSeed
{
    public static readonly SteamItem[] Items =
    [
        new() { AppId = 70, Name = "Half-Life", Price = 9.99m, ReleaseDate = new(1998, 11, 8) },
        new() { AppId = 220, Name = "Half-Life 2", Price = 9.99m, ReleaseDate = new(2004, 11, 16) },
        new() { AppId = 400, Name = "Portal", Price = 9.99m, ReleaseDate = new(2007, 10, 10) },
        new() { AppId = 440, Name = "Team Fortress 2", Price = 0m, ReleaseDate = new(2007, 10, 10) },
        new() { AppId = 550, Name = "Left 4 Dead 2", Price = 9.99m, ReleaseDate = new(2009, 11, 16) },
        new() { AppId = 570, Name = "Dota 2", Price = 0m, ReleaseDate = new(2013, 7, 9) },
        new() { AppId = 620, Name = "Portal 2", Price = 9.99m, ReleaseDate = new(2011, 4, 18) },
        new() { AppId = 730, Name = "Counter-Strike 2", Price = 0m, ReleaseDate = new(2012, 8, 21) },
        new() { AppId = 105600, Name = "Terraria", Price = 9.99m, ReleaseDate = new(2011, 5, 16) },
        new() { AppId = 271590, Name = "Grand Theft Auto V", Price = 29.99m, ReleaseDate = new(2015, 4, 14) },
        new() { AppId = 292030, Name = "The Witcher 3: Wild Hunt", Price = 39.99m, ReleaseDate = new(2015, 5, 18) },
        new() { AppId = 367520, Name = "Hollow Knight", Price = 14.99m, ReleaseDate = new(2017, 2, 24) },
        new() { AppId = 413150, Name = "Stardew Valley", Price = 14.99m, ReleaseDate = new(2016, 2, 26) },
        new() { AppId = 489830, Name = "The Elder Scrolls V: Skyrim Special Edition", Price = 39.99m, ReleaseDate = new(2016, 10, 27) },
        new() { AppId = 646570, Name = "Slay the Spire", Price = 24.99m, ReleaseDate = new(2019, 1, 23) },
        new() { AppId = 892970, Name = "Valheim", Price = 19.99m, ReleaseDate = new(2021, 2, 2) },
        new() { AppId = 1086940, Name = "Baldur's Gate 3", Price = 59.99m, ReleaseDate = new(2023, 8, 3) },
        new() { AppId = 1091500, Name = "Cyberpunk 2077", Price = 59.99m, ReleaseDate = new(2020, 12, 10) },
        new() { AppId = 1145360, Name = "Hades", Price = 24.99m, ReleaseDate = new(2020, 9, 17) },
        new() { AppId = 1245620, Name = "Elden Ring", Price = 59.99m, ReleaseDate = new(2022, 2, 24) },
    ];
}
