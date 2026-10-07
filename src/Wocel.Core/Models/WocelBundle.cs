using System.IO.Compression;
using System.Text.Json;

namespace Wocel.Core.Models;

public class WocelBundle
{
    public string ManifestVersion { get; set; } = "1.0";
    public string AppVersion { get; set; } = "1.0.0";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string Name { get; set; } = "Wocel Project";

    public List<DocumentDocument> Documents { get; set; } = new();
    public List<SpreadsheetDocument> Spreadsheets { get; set; } = new();

    public static async Task SaveToZipAsync(WocelBundle bundle, Stream outputZipStream)
    {
        bundle.ModifiedAt = DateTime.UtcNow;
        using var archive = new ZipArchive(outputZipStream, ZipArchiveMode.Create, leaveOpen: true);

        // 1. Write Manifest
        var manifestEntry = archive.CreateEntry("manifest.json");
        using (var entryStream = manifestEntry.Open())
        {
            await using var writer = new Utf8JsonWriter(entryStream, new JsonWriterOptions { Indented = true });
            JsonSerializer.Serialize(writer, new
            {
                bundle.ManifestVersion,
                bundle.AppVersion,
                bundle.CreatedAt,
                bundle.ModifiedAt,
                bundle.Name,
                DocCount = bundle.Documents.Count,
                SheetCount = bundle.Spreadsheets.Count
            });
            await writer.FlushAsync();
        }

        // 2. Write Documents
        for (int i = 0; i < bundle.Documents.Count; i++)
        {
            var doc = bundle.Documents[i];
            var docEntry = archive.CreateEntry($"docs/doc_{i}_{doc.Id}.json");
            using (var entryStream = docEntry.Open())
            {
                await JsonSerializer.SerializeAsync(entryStream, doc, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        // 3. Write Spreadsheets
        for (int i = 0; i < bundle.Spreadsheets.Count; i++)
        {
            var sheet = bundle.Spreadsheets[i];
            var sheetEntry = archive.CreateEntry($"sheets/sheet_{i}_{sheet.Id}.json");
            using (var entryStream = sheetEntry.Open())
            {
                await JsonSerializer.SerializeAsync(entryStream, sheet, new JsonSerializerOptions { WriteIndented = true });
            }
        }
    }

    public static async Task<WocelBundle> LoadFromZipAsync(Stream inputZipStream)
    {
        var bundle = new WocelBundle();
        using var archive = new ZipArchive(inputZipStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("docs/") && entry.FullName.EndsWith(".json"))
            {
                using var stream = entry.Open();
                var doc = await JsonSerializer.DeserializeAsync<DocumentDocument>(stream);
                if (doc != null) bundle.Documents.Add(doc);
            }
            else if (entry.FullName.StartsWith("sheets/") && entry.FullName.EndsWith(".json"))
            {
                using var stream = entry.Open();
                var sheet = await JsonSerializer.DeserializeAsync<SpreadsheetDocument>(stream);
                if (sheet != null) bundle.Spreadsheets.Add(sheet);
            }
        }

        return bundle;
    }
}
