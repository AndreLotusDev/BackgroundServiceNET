using SteamItems.Web.Data;

namespace SteamItems.Web.Models;

public class ItemSelectionViewModel
{
    public IReadOnlyList<SteamItem> Items { get; init; } = [];

    /// <summary>App ids of the checked items; bound from the checkboxes on post.</summary>
    public List<int> SelectedAppIds { get; set; } = [];
}
