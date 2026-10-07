using Wocel.Core.Events;
using Wocel.Core.Models;
using Wocel.Excel;
using Wocel.Excel.Sessions;
using Wocel.Shell.Services;
using Wocel.Shell.ViewModels;
using Wocel.Word;
using Wocel.Word.Sessions;
using Xunit;

namespace Wocel.Tests;

public class ShellAndBundleTests
{
    [Fact]
    public async Task WocelBundle_PackageAndLoad_Succeeds()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_project_{Guid.NewGuid():N}.wocel");
        try
        {
            var wordSession = new WordDocumentSession("Q1 Report");
            var block = new DocBlock { Type = BlockType.Heading1 };
            block.Inlines.Add(new TextRun { Text = "Báo cáo hợp nhất" });
            wordSession.Document.Blocks.Add(block);

            var excelSession = new ExcelDocumentSession("Q1 Financials");
            excelSession.Document.GetOrCreateActiveSheet().SetValue("A1", "Doanh thu");
            excelSession.Document.GetOrCreateActiveSheet().SetValue("B1", 1000000);

            // Save .wocel compound package
            await CompoundProjectService.SaveCompoundProjectAsync(tempFile, wordSession, excelSession);

            // Load .wocel back
            var (loadedWord, loadedExcel) = await CompoundProjectService.LoadCompoundProjectAsync(tempFile);

            Assert.NotNull(loadedWord);
            Assert.NotNull(loadedExcel);
            Assert.Equal("Q1 Report", loadedWord.Title);
            Assert.Equal("Báo cáo hợp nhất", loadedWord.Document.Blocks[0].Inlines[0].Text);
            Assert.Equal(1000000.0, loadedExcel.Document.GetOrCreateActiveSheet().GetCell("B1").GetNumericValue());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Shell_Workspace_Coordinates_Tabs_And_Interop()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new WordModule());
        registry.RegisterModule(new ExcelModule());

        var eventBus = new EventBus();
        var workspace = new ShellWorkspaceViewModel(registry, eventBus);

        // 1. Create Document Tab
        var docTab = workspace.CreateNewDocumentTab("My Report");
        Assert.Single(workspace.Tabs);
        Assert.Equal("My Report", workspace.ActiveTab?.Title);

        // 2. Create Sheet Tab
        var sheetTab = workspace.CreateNewSpreadsheetTab("My Sheet");
        Assert.Equal(2, workspace.Tabs.Count);
        Assert.Equal("My Sheet", workspace.ActiveTab?.Title);

        // 3. Enable Split View
        workspace.EnableSplitView(docTab, sheetTab);
        Assert.True(workspace.IsSplitViewActive);
        Assert.Equal(docTab, workspace.ActiveTab);
        Assert.Equal(sheetTab, workspace.SecondarySplitTab);

        // 4. Interop Sync: Sync Range from Excel into Word
        var excelSession = (ExcelDocumentSession)sheetTab.Session;
        excelSession.Document.GetOrCreateActiveSheet().SetValue("A1", "Chi phí Marketing");
        excelSession.Document.GetOrCreateActiveSheet().SetValue("B1", "50,000,000");

        var wordSession = (WordDocumentSession)docTab.Session;
        workspace.SyncSheetRangeToWord(excelSession, "A1:B1", wordSession);

        var syncedBlock = wordSession.Document.Blocks.Last();
        Assert.Equal(BlockType.Table, syncedBlock.Type);
        Assert.True(syncedBlock.IsLiveSynced);
        Assert.Equal("Chi phí Marketing", syncedBlock.TableData![0][0]);
    }
}
