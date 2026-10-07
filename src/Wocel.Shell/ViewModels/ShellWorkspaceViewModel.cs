using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Wocel.Core.Contracts;
using Wocel.Core.Events;
using Wocel.Core.Models;
using Wocel.Excel;
using Wocel.Excel.Engine;
using Wocel.Excel.Sessions;
using Wocel.Shell.Services;
using Wocel.Shell.Commands;
using Wocel.Word;
using Wocel.Word.Sessions;

namespace Wocel.Shell.ViewModels;

public class ShellWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly ModuleRegistry _moduleRegistry;
    private readonly IEventBus _eventBus;
    private readonly RecentFilesService _recentFilesService;
    private readonly AutoSaveService _autoSaveService;
    private readonly SettingsService _settingsService;
    private readonly CloudSyncService _cloudSyncService;

    private TabItemViewModel? _activeTab;
    private TabItemViewModel? _secondarySplitTab;
    private bool _isSplitViewActive;
    private bool _isWelcomeScreenVisible = true;
    private int _selectedHomeTab = 0; // 0 = Word, 1 = Excel
    private string _currentFormula = "";
    private string _currentCellAddress = "A1";
    private string _selectedCellAddress = "";
    private int _activeSheetIndex = 0;
    private string _wordContentText = "";
    private bool _isBold = false;
    private bool _isItalic = false;
    private bool _isUnderline = false;
    private string _currentFontFamily = "Segoe UI";
    private int _currentFontSize = 14;

    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();
    public ObservableCollection<RecentFileItem> RecentWordFiles { get; } = new();
    public ObservableCollection<RecentFileItem> RecentExcelFiles { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsWelcomeScreenVisible
    {
        get => _isWelcomeScreenVisible;
        set => SetField(ref _isWelcomeScreenVisible, value);
    }

    public int SelectedHomeTab
    {
        get => _selectedHomeTab;
        set => SetField(ref _selectedHomeTab, value);
    }

    public TabItemViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetField(ref _activeTab, value))
            {
                OnActiveTabChanged();
            }
        }
    }

    public TabItemViewModel? SecondarySplitTab
    {
        get => _secondarySplitTab;
        set => SetField(ref _secondarySplitTab, value);
    }

    public bool IsSplitViewActive
    {
        get => _isSplitViewActive;
        set => SetField(ref _isSplitViewActive, value);
    }

    public string CurrentCellAddress
    {
        get => _currentCellAddress;
        set => SetField(ref _currentCellAddress, value);
    }

    public string SelectedCellAddress
    {
        get => _selectedCellAddress;
        set => SetField(ref _selectedCellAddress, value);
    }

    public int ActiveSheetIndex
    {
        get => _activeSheetIndex;
        set => SetField(ref _activeSheetIndex, value);
    }

    public string CurrentFormula
    {
        get => _currentFormula;
        set => SetField(ref _currentFormula, value);
    }

    public string WordContentText
    {
        get => _wordContentText;
        // Chỉ để hiển thị/đọc. Việc soạn thảo do WordCanvasEditor làm trực tiếp trên
        // DocumentDocument, nên gán vào đây KHÔNG được dựng lại tài liệu — làm vậy
        // sẽ xoá sạch định dạng của từng đoạn chữ mỗi lần chuyển thẻ.
        set => SetField(ref _wordContentText, value);
    }

    public bool IsBold
    {
        get => _isBold;
        set => SetField(ref _isBold, value);
    }

    public bool IsItalic
    {
        get => _isItalic;
        set => SetField(ref _isItalic, value);
    }

    public bool IsUnderline
    {
        get => _isUnderline;
        set => SetField(ref _isUnderline, value);
    }

    public string CurrentFontFamily
    {
        get => _currentFontFamily;
        set => SetField(ref _currentFontFamily, value);
    }

    public int CurrentFontSize
    {
        get => _currentFontSize;
        set => SetField(ref _currentFontSize, value);
    }

    // Commands
    public ICommand SaveCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand NewWordCommand { get; }
    public ICommand NewExcelCommand { get; }
    public ICommand ToggleBoldCommand { get; }
    public ICommand ToggleItalicCommand { get; }
    public ICommand ToggleUnderlineCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand QuitCommand { get; }

    public bool CloudSyncEnabled => EnvironmentService.EnableCloudSync && _cloudSyncService.IsConfigured;

    public ShellWorkspaceViewModel(ModuleRegistry moduleRegistry, IEventBus eventBus, RecentFilesService? recentFilesService = null, AutoSaveService? autoSaveService = null, SettingsService? settingsService = null, CloudSyncService? cloudSyncService = null)
    {
        _moduleRegistry = moduleRegistry;
        _eventBus = eventBus;
        _recentFilesService = recentFilesService ?? new RecentFilesService();
        _autoSaveService = autoSaveService ?? new AutoSaveService();
        _settingsService = settingsService ?? new SettingsService();
        _cloudSyncService = cloudSyncService ?? new CloudSyncService();

        // Initialize commands
        SaveCommand = new RelayCommand(() => { /* Save logic will be handled by MainWindow */ });
        OpenCommand = new RelayCommand(() => { /* Open logic will be handled by MainWindow */ });
        NewWordCommand = new RelayCommand(() => CreateNewWordDocument());
        NewExcelCommand = new RelayCommand(() => CreateNewExcelWorkbook());
        ToggleBoldCommand = new RelayCommand(() => ToggleBold());
        ToggleItalicCommand = new RelayCommand(() => ToggleItalic());
        ToggleUnderlineCommand = new RelayCommand(() => ToggleUnderline());
        CloseTabCommand = new RelayCommand(() => { if (ActiveTab != null) CloseTab(ActiveTab); });
        QuitCommand = new RelayCommand(() => { /* Quit logic will be handled by MainWindow */ });

        RefreshRecentFiles();
    }

    public void RefreshRecentFiles()
    {
        RecentWordFiles.Clear();
        foreach (var file in _recentFilesService.GetRecentFiles(OfficeModuleType.Word))
        {
            RecentWordFiles.Add(file);
        }

        RecentExcelFiles.Clear();
        foreach (var file in _recentFilesService.GetRecentFiles(OfficeModuleType.Excel))
        {
            RecentExcelFiles.Add(file);
        }
    }

    public TabItemViewModel CreateNewDocumentTab(string? title = "Document 1.docx")
        => CreateNewWordDocument(title);

    public TabItemViewModel CreateNewSpreadsheetTab(string? title = "Workbook 1.xlsx")
        => CreateNewExcelWorkbook(title);

    public TabItemViewModel CreateNewWordDocument(string? title = "Document 1.docx")
    {
        var module = _moduleRegistry.GetModule(OfficeModuleType.Word);
        var session = (WordDocumentSession)module.CreateNewSession(title);
        var tab = new TabItemViewModel(session, title ?? "Document 1.docx", OfficeModuleType.Word);
        
        Tabs.Add(tab);
        ActiveTab = tab;
        IsWelcomeScreenVisible = false;
        
        // Enable auto-save for the new document
        _autoSaveService.EnableAutoSave(session, session.Id);
        
        return tab;
    }

    public TabItemViewModel CreateNewExcelWorkbook(string? title = "Workbook 1.xlsx")
    {
        var module = _moduleRegistry.GetModule(OfficeModuleType.Excel);
        var session = (ExcelDocumentSession)module.CreateNewSession(title);
        var tab = new TabItemViewModel(session, title ?? "Workbook 1.xlsx", OfficeModuleType.Excel);
        
        Tabs.Add(tab);
        ActiveTab = tab;
        IsWelcomeScreenVisible = false;
        
        // Enable auto-save for the new workbook
        _autoSaveService.EnableAutoSave(session, session.Id);
        
        return tab;
    }

    public async Task<TabItemViewModel> OpenFileByPathAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".wocel")
        {
            var (wordSession, excelSession) = await CompoundProjectService.LoadCompoundProjectAsync(filePath);
            TabItemViewModel? firstTab = null;

            if (wordSession != null)
            {
                var docTab = new TabItemViewModel(wordSession, wordSession.Title, OfficeModuleType.Word);
                Tabs.Add(docTab);
                firstTab ??= docTab;
            }

            if (excelSession != null)
            {
                var sheetTab = new TabItemViewModel(excelSession, excelSession.Title, OfficeModuleType.Excel);
                Tabs.Add(sheetTab);
                firstTab ??= sheetTab;
            }

            if (firstTab == null)
            {
                throw new InvalidDataException("Tệp .wocel không chứa tài liệu nào.");
            }

            _recentFilesService.AddRecentFile(filePath, OfficeModuleType.Word);
            RefreshRecentFiles();
            ActiveTab = firstTab;
            IsWelcomeScreenVisible = false;
            return firstTab;
        }

        var module = _moduleRegistry.FindModuleForExtension(filePath);
        if (module == null)
        {
            throw new NotSupportedException($"Định dạng '{ext}' chưa được hỗ trợ bởi Wocel.");
        }

        var session = await module.OpenSessionAsync(filePath);
        var tab = new TabItemViewModel(session, Path.GetFileName(filePath), module.ModuleType);
        Tabs.Add(tab);
        
        _recentFilesService.AddRecentFile(filePath, module.ModuleType);
        RefreshRecentFiles();

        // Enable auto-save for the opened file
        _autoSaveService.EnableAutoSave(session, session.Id);

        ActiveTab = tab;
        IsWelcomeScreenVisible = false;
        return tab;
    }

    public void ShowWelcomeScreen()
    {
        RefreshRecentFiles();
        IsWelcomeScreenVisible = true;
    }

    /// <summary>Rời màn hình chào để vào khu làm việc (dùng khi mở thẳng bộ công cụ tệp).</summary>
    public void EnsureWorkspaceVisible()
    {
        IsWelcomeScreenVisible = false;
    }

    public void CloseTab(TabItemViewModel tab)
    {
        // Disable auto-save for this tab
        _autoSaveService.DisableAutoSave(tab.Session.Id);
        
        tab.Session.Dispose();
        Tabs.Remove(tab);
        if (ActiveTab == tab)
        {
            ActiveTab = Tabs.LastOrDefault();
            if (ActiveTab == null)
            {
                IsWelcomeScreenVisible = true;
            }
        }
        if (SecondarySplitTab == tab)
        {
            SecondarySplitTab = null;
            IsSplitViewActive = false;
        }
    }

    public void EnableSplitView(TabItemViewModel primaryTab, TabItemViewModel secondaryTab)
    {
        ActiveTab = primaryTab;
        SecondarySplitTab = secondaryTab;
        IsSplitViewActive = true;
        IsWelcomeScreenVisible = false;
    }

    public void DisableSplitView()
    {
        SecondarySplitTab = null;
        IsSplitViewActive = false;
    }

    public void SyncSheetRangeToWord(ExcelDocumentSession sheetSession, string range, WordDocumentSession wordSession)
    {
        var sheet = sheetSession.Document.GetOrCreateActiveSheet();
        var rangeParts = range.Split(':');
        if (rangeParts.Length != 2) return;

        var start = new CellAddress(rangeParts[0]);
        var end = new CellAddress(rangeParts[1]);

        var minRow = Math.Min(start.Row, end.Row);
        var maxRow = Math.Max(start.Row, end.Row);
        var minCol = Math.Min(start.Column, end.Column);
        var maxCol = Math.Max(start.Column, end.Column);

        var tableData = new List<List<string>>();
        for (int r = minRow; r <= maxRow; r++)
        {
            var rowList = new List<string>();
            for (int c = minCol; c <= maxCol; c++)
            {
                var addr = CellAddress.ToA1(r, c);
                var cell = sheet.GetCell(addr);
                rowList.Add(cell.GetDisplayString());
            }
            tableData.Add(rowList);
        }

        var block = new DocBlock
        {
            Type = BlockType.Table,
            TableData = tableData,
            EmbeddedSheetId = sheetSession.Id,
            EmbeddedRange = range,
            IsLiveSynced = true
        };

        wordSession.Document.Blocks.Add(block);
        wordSession.MarkDirty();
    }

    private void OnActiveTabChanged()
    {
        if (ActiveTab == null)
        {
            WordContentText = "";
            return;
        }

        if (ActiveTab.ModuleType == OfficeModuleType.Word && ActiveTab.Session is WordDocumentSession wordSession)
        {
            WordContentText = wordSession.Document.ToPlainText();
        }
    }

    /// <summary>
    /// Nạp văn bản thuần vào tài liệu Word đang mở (dùng khi tạo tài liệu mới từ
    /// nội dung PDF). Đây là thao tác cố ý ghi đè, không phải đồng bộ tự động.
    /// </summary>
    public void LoadPlainTextIntoWordDocument(string text)
    {
        if (ActiveTab?.Session is not WordDocumentSession wordSession) return;

        wordSession.Document.Blocks.Clear();

        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var block = new DocBlock { Type = BlockType.Paragraph };
            block.Inlines.Add(new TextRun
            {
                Text = line,
                FontFamily = _currentFontFamily,
                FontSize = _currentFontSize
            });
            wordSession.Document.Blocks.Add(block);
        }

        WordContentText = text;
        wordSession.MarkDirty();
    }

    public void ToggleBold()
    {
        IsBold = !IsBold;
    }

    public void ToggleItalic()
    {
        IsItalic = !IsItalic;
    }

    public void ToggleUnderline()
    {
        IsUnderline = !IsUnderline;
    }

    public void SetFontFamily(string fontFamily)
    {
        CurrentFontFamily = fontFamily;
    }

    public void SetFontSize(int fontSize)
    {
        CurrentFontSize = fontSize;
    }

    public void Copy()
    {
        // Copy is handled by the built-in clipboard functionality of TextBox controls
        // This method is a placeholder for future custom copy operations
    }

    public void Cut()
    {
        // Cut is handled by the built-in clipboard functionality of TextBox controls
        // This method is a placeholder for future custom cut operations
    }

    public void Paste()
    {
        // Paste is handled by the built-in clipboard functionality of TextBox controls
        // This method is a placeholder for future custom paste operations
    }

    public void AutoSum()
    {
        if (ActiveTab?.Session is ExcelDocumentSession excelSession)
        {
            var sheet = excelSession.Document.GetOrCreateActiveSheet();
            var currentAddr = CurrentCellAddress;
            var currentCell = new CellAddress(currentAddr);
            
            // Auto-sum the column above the current cell
            if (currentCell.Row > 1)
            {
                var startAddr = CellAddress.ToA1(1, currentCell.Column);
                var endAddr = CellAddress.ToA1(currentCell.Row - 1, currentCell.Column);
                var formula = $"=SUM({startAddr}:{endAddr})";
                
                UpdateExcelCell(currentAddr, formula);
                CurrentFormula = formula;
            }
        }
    }

    public async Task<bool> SaveToCloudAsync()
    {
        if (!CloudSyncEnabled || ActiveTab?.Session == null) return false;

        try
        {
            var success = await _cloudSyncService.SaveDocumentAsync(ActiveTab.Session, ActiveTab.Session.Id);
            
            if (success)
            {
                // Save metadata
                var metadata = new CloudDocumentMetadata
                {
                    Id = ActiveTab.Session.Id,
                    Title = ActiveTab.Title,
                    ModuleType = ActiveTab.ModuleType,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                await _cloudSyncService.SaveDocumentMetadataAsync(metadata);
            }
            
            return success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TabItemViewModel?> LoadFromCloudAsync(string documentId, OfficeModuleType moduleType)
    {
        if (!CloudSyncEnabled) return null;

        try
        {
            var stream = await _cloudSyncService.LoadDocumentAsync(documentId, moduleType);
            if (stream == null) return null;

            var module = _moduleRegistry.GetModule(moduleType);
            var session = await module.OpenSessionAsync(documentId);
            
            var tab = new TabItemViewModel(session, session.Title, moduleType);
            Tabs.Add(tab);
            ActiveTab = tab;
            IsWelcomeScreenVisible = false;
            
            // Enable auto-save
            _autoSaveService.EnableAutoSave(session, session.Id);
            
            return tab;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<CloudDocumentMetadata>> GetCloudDocumentsAsync()
    {
        if (!CloudSyncEnabled) return new List<CloudDocumentMetadata>();
        return await _cloudSyncService.GetDocumentMetadataAsync();
    }

    public void UpdateExcelCell(string address, string value)
    {
        if (ActiveTab?.Session is ExcelDocumentSession excelSession)
        {
            var sheet = excelSession.Document.GetOrCreateActiveSheet();
            sheet.SetValue(address, value);
            excelSession.Recalculate();
            excelSession.MarkDirty();
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
