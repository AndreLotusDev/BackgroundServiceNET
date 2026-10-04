using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamItems.Web.Data;
using SteamItems.Web.Export;
using SteamItems.Web.Models;
using SteamItems.Web.Storage;

namespace SteamItems.Web.Controllers;

[Authorize]
public class ItemsController(WebDbContext db, SelectionExporter exporter, ExportSubmitter submitter) : Controller
{
    // Subject id issued by SteamItems.Identity (claims are not remapped, see Program.cs).
    private string UserId => User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("Authenticated user has no 'sub' claim.");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var selected = await db.UserSelections
            .Where(s => s.UserId == UserId)
            .Select(s => s.AppId)
            .ToListAsync(cancellationToken);

        return View(new ItemSelectionViewModel
        {
            Items = await LoadCatalogAsync(cancellationToken),
            SelectedAppIds = selected,
        });
    }

    /// <summary>Replaces the user's saved selection with the checked items.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ItemSelectionViewModel model, CancellationToken cancellationToken)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        var appIds = model.SelectedAppIds
            .Intersect(catalog.Select(i => i.AppId))
            .ToList();

        if (appIds.Count == 0)
        {
            ModelState.AddModelError(nameof(model.SelectedAppIds), "Select at least one item.");
            return View(new ItemSelectionViewModel { Items = catalog });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.UserSelections.Where(s => s.UserId == UserId).ExecuteDeleteAsync(cancellationToken);
        db.UserSelections.AddRange(appIds.Select(appId => new UserSelection { UserId = UserId, AppId = appId }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["StatusMessage"] = $"Saved {appIds.Count} selected item(s).";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Downloads the saved selection as an <c>.xlsx</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var export = await exporter.ExportAsync(UserId, cancellationToken);
        if (export is null)
        {
            TempData["ErrorMessage"] = "Save a selection before exporting.";
            return RedirectToAction(nameof(Index));
        }

        return File(export.Content, ExcelExport.ContentType, export.FileName);
    }

    /// <summary>Uploads the saved selection as an <c>.xlsx</c> for the Worker, then shows the export list.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        ExportRecord? export;
        try
        {
            export = await submitter.SubmitAsync(UserId, cancellationToken);
        }
        catch (FileStorageException)
        {
            // Already logged by the storage; the user gets a retryable message instead of an error page.
            TempData["ErrorMessage"] = "The file could not be uploaded because file storage is unavailable. Try again in a moment.";
            return RedirectToAction(nameof(Index));
        }

        if (export is null)
        {
            TempData["ErrorMessage"] = "Save a selection before uploading.";
            return RedirectToAction(nameof(Index));
        }

        TempData["StatusMessage"] = $"Submitted {export.FileName} ({export.ItemCount} item(s)) for processing.";
        return RedirectToAction(nameof(ExportsController.Index), "Exports");
    }

    private async Task<List<SteamItem>> LoadCatalogAsync(CancellationToken cancellationToken) =>
        await db.SteamItems.AsNoTracking().OrderBy(i => i.Name).ToListAsync(cancellationToken);
}
