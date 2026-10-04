namespace SteamItems.Web.Data;

/// <summary>A game from the Steam store; <see cref="AppId"/> is the Steam app id.</summary>
public class SteamItem
{
    public int AppId { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public DateOnly ReleaseDate { get; set; }
}

/// <summary>An item a user picked; the user is the <c>sub</c> claim from SteamItems.Identity.</summary>
public class UserSelection
{
    public required string UserId { get; set; }
    public int AppId { get; set; }
    public SteamItem Item { get; set; } = null!;
}
