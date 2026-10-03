# 06 – Excel export

Turn the selected items into an `.xlsx` file with ClosedXML.

## Questions to resolve first

1. Final column layout (this is the contract with the Worker). Suggested: `AppId | Name | Price | ReleaseDate`, header on row 1.
2. Should the user also be able to download the file, or is it only uploaded?
3. Do we add metadata (user id, export id) in the file, or only in the S3 object metadata/key?

## Scope

- Excel writer in Web using the agreed columns (from `Contracts` if task 01 kept it).
- Unit tests for the writer.

## Acceptance criteria

- [ ] Selected items produce an `.xlsx` with one header row + one row per item.
- [ ] File opens in Excel/LibreOffice without warnings.
- [ ] Unit test reads the generated file back and checks every cell.
- [ ] Column names/order defined in one place only.
