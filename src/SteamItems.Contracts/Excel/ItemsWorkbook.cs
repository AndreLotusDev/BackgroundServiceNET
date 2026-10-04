namespace SteamItems.Contracts.Excel;

/// <summary>
/// Layout of the exported <c>.xlsx</c>: the contract between the Web (writer) and the Worker (reader).
/// Change columns, sheet name or property names here only.
/// </summary>
public static class ItemsWorkbook
{
    public const string SheetName = "Items";
    public const int HeaderRow = 1;
    public const int FirstDataRow = 2;

    public static readonly ItemsColumn AppId = new(1, "AppId");
    public static readonly ItemsColumn Name = new(2, "Name");
    public static readonly ItemsColumn Price = new(3, "Price");
    public static readonly ItemsColumn ReleaseDate = new(4, "ReleaseDate");

    /// <summary>All columns, in sheet order.</summary>
    public static readonly IReadOnlyList<ItemsColumn> Columns = [AppId, Name, Price, ReleaseDate];

    /// <summary>Names of the workbook custom document properties that carry <see cref="ExportInfo"/>.</summary>
    public static class Properties
    {
        public const string ExportId = "SteamItems.ExportId";
        public const string UserId = "SteamItems.UserId";
        public const string CreatedAt = "SteamItems.CreatedAt";
    }
}

/// <summary>A column of the items sheet; <see cref="Number"/> is 1-based like Excel.</summary>
public sealed record ItemsColumn(int Number, string Header);

/// <summary>One data row of the items sheet.</summary>
public sealed record ItemRow(int AppId, string Name, decimal Price, DateOnly ReleaseDate);

/// <summary>Who exported the file and when; stored as workbook properties, not as sheet rows.</summary>
public sealed record ExportInfo(Guid ExportId, string UserId, DateTimeOffset CreatedAt);
