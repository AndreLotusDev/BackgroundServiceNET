# 06 – Excel export

Turn the selected items into an `.xlsx` file with ClosedXML.

## Questions to resolve first

1. Final column layout (this is the contract with the Worker). Suggested: `AppId | Name | Price | ReleaseDate`, header on row 1.
   → Answer: `AppId | Name | Price | ReleaseDate`, header on row 1, data from row 2, one sheet named `Items`. `AppId` and `Price` are numbers, `ReleaseDate` is a real Excel date (format `yyyy-mm-dd`), not text. Defined in `SteamItems.Contracts/Excel/ItemsWorkbook.cs`.
2. Should the user also be able to download the file, or is it only uploaded?
   → Answer: Download too. **Download Excel** button on `/Items` (`GET /Items/Export`) and `GET /api/items/export` (bearer, `ApiScope`). Both export the user's saved selection.
3. Do we add metadata (user id, export id) in the file, or only in the S3 object metadata/key?
   → Answer: In the file too, as workbook custom document properties (`SteamItems.ExportId`, `SteamItems.UserId`, `SteamItems.CreatedAt`), so the sheet stays one header row + data rows. Task 07 can still put them in the key/metadata.

## Scope

- Excel writer in Web using the agreed columns (from `Contracts` if task 01 kept it).
- Unit tests for the writer.

## Acceptance criteria

- [x] Selected items produce an `.xlsx` with one header row + one row per item.
- [x] File opens in Excel/LibreOffice without warnings.
- [x] Unit test reads the generated file back and checks every cell.
- [x] Column names/order defined in one place only.

Verified with `dotnet test` (8 tests in `tests/SteamItems.Web.Tests`) and by running Identity + Web (`dotnet run --launch-profile https`):
the writer tests read the file back with ClosedXML and check the sheet name, row count, header text and every data cell with its type (number/text/date), plus the document properties; an empty list gives only the header.
"Opens without warnings" is checked with the Open XML SDK validator (`OpenXmlValidator`, no errors); LibreOffice is not installed on this machine, so the file was not opened in a desktop app.
`SelectionExporter` tests (in-memory SQLite) check that only the user's items are exported, sorted by name, and that no selection returns nothing.
Running: anonymous `/Items/Export` → 302 to `/connect/authorize`. Logged in as `alice`, `/Items/Export` returns `200` with the xlsx content type and `steam-items-<utc timestamp>.xlsx`; the sheet has the header + her 2 saved items, and `docProps/custom.xml` has her `sub`, the export id and the time.
`GET /api/items/export` returns `401` without a token and the same file with an `api` access token.

## Notes

- Writer: `SteamItems.Web/Export/ItemsExcelWriter.cs` (`IItemsExcelWriter`, writes to a `Stream`). `SelectionExporter` loads the saved selection and returns an `ExcelExport` (info, file name, bytes); task 07 uploads `Content`.
- The Worker (task 09) should read with `ItemsWorkbook` column numbers, not by header text lookups spread around the code.
- `ExportId` is a UUID v7 (`Guid.CreateVersion7`), so ids sort by time.
- Tests pin the header names as literals on purpose: renaming a column in `ItemsWorkbook` breaks the Worker contract, so the test should fail.
