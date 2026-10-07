using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wocel.Core.Pdf;
using Wocel.Core.Services;

namespace Wocel.Shell.Views;

/// <summary>
/// Bộ công cụ xử lý tệp kiểu Smallpdf — chạy hoàn toàn ngoại tuyến trên máy người dùng.
/// Mọi thao tác gọi xuống <see cref="PdfToolkit"/> trong Wocel.Core.
/// </summary>
public partial class PdfToolsPanel : Border
{
    private record ToolDef(string Id, string Icon, string Name, string Desc, string Category, string Color);

    private static readonly ToolDef[] Tools =
    {
        // ── Tổ chức trang ────────────────────────────────────────────────
        new("merge",        "🔗", "Gộp PDF",            "Nối nhiều tệp PDF thành một, theo thứ tự bạn sắp xếp",        "Tổ chức trang", "#8E44AD"),
        new("split",        "✂",  "Tách PDF",           "Mỗi trang một tệp, chia theo nhóm N trang, hoặc theo khoảng", "Tổ chức trang", "#2980B9"),
        new("pages",        "📑", "Giữ / xoá trang",    "Chỉ giữ lại những trang bạn cần, hoặc bỏ hẳn trang thừa",     "Tổ chức trang", "#1A5276"),
        new("reorder",      "🔃", "Sắp xếp lại trang",  "Đổi thứ tự trang hoặc đảo ngược toàn bộ tài liệu",           "Tổ chức trang", "#16A085"),
        new("rotate",       "🔄", "Xoay trang",         "Xoay 90°, 180°, 270° cho tất cả hoặc từng trang",            "Tổ chức trang", "#117A65"),
        new("page_setup",   "📐", "Khổ giấy & cắt lề",  "Đưa mọi trang về cùng khổ giấy và cắt bớt lề thừa",          "Tổ chức trang", "#2E86C1"),

        // ── Chuyển đổi ───────────────────────────────────────────────────
        new("pdf_to_text",  "📄", "PDF → Văn bản",      "Trích toàn bộ chữ ra .txt, đọc đúng dấu tiếng Việt",         "Chuyển đổi",    "#185ABD"),
        new("pdf_to_csv",   "📊", "PDF → Excel (CSV)",  "Nhận diện bảng trong PDF và xuất ra .csv mở bằng Excel",     "Chuyển đổi",    "#107C41"),
        new("pdf_to_images","🖼",  "Tách ảnh khỏi PDF",  "Lưu mọi ảnh nhúng trong tệp ra .jpg/.png",                   "Chuyển đổi",    "#D35400"),
        new("images_to_pdf","📷", "Ảnh → PDF",          "Ghép nhiều ảnh JPG/PNG/BMP thành một tệp PDF",               "Chuyển đổi",    "#E67E22"),
        new("to_pdf",       "📝", "Văn bản/Bảng → PDF", "Chuyển .txt, .md, .csv thành PDF — tệp bảng tự vẽ thành khung", "Chuyển đổi", "#6C3483"),

        // ── Hệ thống ─────────────────────────────────────────────────────
        new("logs",         "📜", "Nhật ký hoạt động",  "Xem lại mọi thao tác đã chạy và lỗi phát sinh để truy vết",  "Hệ thống",      "#566573")
    };

    // ── Giao diện ────────────────────────────────────────────────────────
    private readonly StackPanel _toolList = new() { Spacing = 4 };
    private readonly TextBox _searchBox;
    private readonly StackPanel _fileList = new() { Spacing = 4 };
    private readonly TextBlock _fileHint;
    private readonly Border _workCard;
    private readonly StackPanel _workContent = new() { Spacing = 12 };
    private readonly TextBlock _statusText;
    private readonly Border _statusCard;
    private readonly TextBox _outputBox;
    private readonly ScrollViewer _outputViewer;
    private readonly TextBox _passwordBox;

    private readonly List<string> _files = new();
    private readonly List<Border> _toolButtons = new();
    private ToolDef? _selectedTool;
    private Func<Task>? _primaryAction;
    private string _lastStatusMessage = string.Empty;
    private string? _lastOutputFolder;

    public event Action<string>? LogMessage;

    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#107C41"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#111827"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#E5E7EB"));

    public PdfToolsPanel()
    {
        Background = new SolidColorBrush(Color.Parse("#F5F6F8"));

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };

        // ── Header ──────────────────────────────────────────────────────
        var headerStack = new StackPanel { Spacing = 3 };
        headerStack.Children.Add(new TextBlock
        {
            Text = "🛠  Bộ công cụ xử lý tệp",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = Ink
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = $"{Tools.Length} công cụ PDF, ảnh và bảng tính — xử lý ngay trên máy bạn, không gửi tệp lên bất kỳ máy chủ nào.",
            FontSize = 12,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap
        });

        var header = new Border
        {
            Background = Brushes.White,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 14),
            Child = headerStack
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // ── Thân ────────────────────────────────────────────────────────
        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("290,*") };

        _searchBox = new TextBox
        {
            Watermark = "🔍 Tìm công cụ…",
            Height = 32,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _searchBox.PropertyChanged += (s, e) =>
        {
            if (e.Property == TextBox.TextProperty) RebuildToolList(_searchBox.Text);
        };

        var sidebarStack = new StackPanel();
        sidebarStack.Children.Add(_searchBox);
        sidebarStack.Children.Add(_toolList);

        var sidebar = new Border
        {
            Background = Brushes.White,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(12, 14),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = sidebarStack
            }
        };
        Grid.SetColumn(sidebar, 0);
        main.Children.Add(sidebar);

        // ── Vùng làm việc ───────────────────────────────────────────────
        _fileHint = new TextBlock
        {
            Text = "Kéo thả tệp vào đây, hoặc bấm “Chọn tệp”.",
            FontSize = 12,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap
        };

        // Phải tạo trước BuildFilesCard() vì thẻ tệp có chứa ô mật khẩu này.
        _passwordBox = new TextBox
        {
            Watermark = "Mật khẩu mở tệp (nếu tệp có đặt mật khẩu)",
            PasswordChar = '•',
            Height = 30,
            FontSize = 12
        };

        var filesCard = BuildFilesCard();

        _workContent.Children.Add(new TextBlock
        {
            Text = "Chọn một công cụ ở cột bên trái để bắt đầu.",
            FontSize = 13,
            Foreground = Muted
        });

        _workCard = Card(_workContent);

        _statusText = new TextBlock
        {
            FontSize = 12.5,
            Foreground = Accent,
            TextWrapping = TextWrapping.Wrap
        };
        _statusCard = Card(_statusText);
        _statusCard.IsVisible = false;

        _outputBox = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace"),
            FontSize = 11.5,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.NoWrap
        };
        _outputViewer = new ScrollViewer
        {
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _outputBox,
            IsVisible = false
        };
        var outputCard = Card(_outputViewer);
        outputCard.IsVisible = false;
        _outputViewer.PropertyChanged += (s, e) =>
        {
            if (e.Property == IsVisibleProperty) outputCard.IsVisible = _outputViewer.IsVisible;
        };

        var workStack = new StackPanel { Spacing = 12, Margin = new Thickness(20, 16, 20, 24) };
        workStack.Children.Add(filesCard);
        workStack.Children.Add(_workCard);
        workStack.Children.Add(_statusCard);
        workStack.Children.Add(outputCard);

        var workScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = workStack
        };
        Grid.SetColumn(workScroll, 1);
        main.Children.Add(workScroll);

        Grid.SetRow(main, 1);
        root.Children.Add(main);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnFileDrop);
        AddHandler(DragDrop.DragOverEvent, (s, e) => e.DragEffects = DragDropEffects.Copy);

        Child = root;
        RebuildToolList(null);
        RefreshFileList();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  KHUNG GIAO DIỆN DÙNG CHUNG
    // ─────────────────────────────────────────────────────────────────────
    private static Border Card(Control content) => new()
    {
        Background = Brushes.White,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(18, 16),
        Child = content
    };

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.Bold,
        Foreground = Ink
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBox Field(string watermark, string? initial = null, double width = double.NaN)
    {
        var box = new TextBox { Watermark = watermark, Text = initial ?? string.Empty, Height = 30, FontSize = 12 };
        if (!double.IsNaN(width)) box.Width = width;
        return box;
    }

    private static ComboBox Choice(IEnumerable<string> items, int selected = 0, double width = 190)
    {
        var combo = new ComboBox { Width = width, Height = 30, FontSize = 12 };
        foreach (var item in items) combo.Items.Add(new ComboBoxItem { Content = item });

        // Phải đặt sau khi đã thêm mục, nếu không lựa chọn mặc định bị bỏ qua
        // và ô hiện ra trống trơn.
        combo.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, combo.Items.Count - 1));
        return combo;
    }

    private static NumericUpDown Number(double value, double min, double max, double increment = 1, double width = 110) => new()
    {
        Value = (decimal)value,
        Minimum = (decimal)min,
        Maximum = (decimal)max,
        Increment = (decimal)increment,
        Width = width,
        Height = 30,
        FontSize = 12
    };

    private static Control Labeled(string label, Control control, double labelWidth = 150)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = labelWidth,
            FontSize = 12,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        control.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(control);
        return row;
    }

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var control in controls)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(control);
        }
        return row;
    }

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        Background = Accent,
        Foreground = Brushes.White,
        FontWeight = FontWeight.SemiBold,
        FontSize = 13,
        Padding = new Thickness(20, 10),
        CornerRadius = new CornerRadius(6),
        HorizontalAlignment = HorizontalAlignment.Left,
        BorderThickness = new Thickness(0)
    };

    private static Button SecondaryButton(string text) => new()
    {
        Content = text,
        Background = new SolidColorBrush(Color.Parse("#F3F4F6")),
        Foreground = Ink,
        FontSize = 12,
        Padding = new Thickness(12, 6),
        CornerRadius = new CornerRadius(5),
        BorderBrush = Line,
        BorderThickness = new Thickness(1)
    };

    // ─────────────────────────────────────────────────────────────────────
    //  DANH SÁCH CÔNG CỤ
    // ─────────────────────────────────────────────────────────────────────
    private void RebuildToolList(string? filter)
    {
        _toolList.Children.Clear();
        _toolButtons.Clear();

        var normalized = (filter ?? string.Empty).Trim().ToLowerInvariant();

        foreach (var category in Tools.Select(t => t.Category).Distinct())
        {
            var matches = Tools.Where(t => t.Category == category && Matches(t, normalized)).ToList();
            if (matches.Count == 0) continue;

            _toolList.Children.Add(new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = Muted,
                Margin = new Thickness(6, 12, 0, 4)
            });

            foreach (var tool in matches)
            {
                var button = BuildToolButton(tool);
                _toolButtons.Add(button);
                _toolList.Children.Add(button);
            }
        }

        if (_toolList.Children.Count == 0)
        {
            _toolList.Children.Add(new TextBlock
            {
                Text = "Không tìm thấy công cụ nào phù hợp.",
                FontSize = 12,
                Foreground = Muted,
                Margin = new Thickness(6, 14, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private static bool Matches(ToolDef tool, string filter)
    {
        if (filter.Length == 0) return true;
        return (tool.Name + " " + tool.Desc + " " + tool.Category + " " + tool.Id)
            .ToLowerInvariant().Contains(filter);
    }

    private Border BuildToolButton(ToolDef tool)
    {
        var badge = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse(tool.Color)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = tool.Icon,
                FontSize = 13,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var textStack = new StackPanel { Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock { Text = tool.Name, FontWeight = FontWeight.SemiBold, FontSize = 12.5, Foreground = Ink });
        textStack.Children.Add(new TextBlock { Text = tool.Desc, FontSize = 10, Foreground = Muted, TextWrapping = TextWrapping.Wrap });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*") };
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(badge);
        grid.Children.Add(textStack);

        var button = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid,
            Tag = tool
        };

        button.PointerPressed += (s, e) => SelectTool(tool);
        return button;
    }

    private void SelectTool(ToolDef tool)
    {
        _selectedTool = tool;

        foreach (var button in _toolButtons)
        {
            bool active = ReferenceEquals(button.Tag, tool);
            button.Background = active ? new SolidColorBrush(Color.Parse("#EAF6EF")) : Brushes.Transparent;
            button.BorderBrush = active ? Accent : Brushes.Transparent;
        }

        BuildWorkPanel(tool);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  KHU VỰC TỆP
    // ─────────────────────────────────────────────────────────────────────
    private Border BuildFilesCard()
    {
        var stack = new StackPanel { Spacing = 10 };

        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = SectionTitle("📂  Tệp đầu vào");
        Grid.SetColumn(title, 0);
        titleRow.Children.Add(title);

        var buttons = Row(SecondaryButton("Chọn tệp…"), SecondaryButton("Xoá hết"));
        ((Button)buttons.Children[0]).Click += async (s, e) => await BrowseFilesAsync();
        ((Button)buttons.Children[1]).Click += (s, e) =>
        {
            _files.Clear();
            RefreshFileList();
        };
        Grid.SetColumn(buttons, 1);
        titleRow.Children.Add(buttons);

        stack.Children.Add(titleRow);
        stack.Children.Add(_fileHint);
        stack.Children.Add(_fileList);
        stack.Children.Add(_passwordBox);

        return Card(stack);
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;

        var files = e.Data.GetFiles();
        if (files == null) return;

        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            if (File.Exists(path) && !_files.Contains(path)) _files.Add(path);
        }

        RefreshFileList();
    }

    private async Task BrowseFilesAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn tệp cần xử lý",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Mọi tệp hỗ trợ")
                    { Patterns = new[] { "*.pdf", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.txt", "*.md", "*.csv" } },
                new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } },
                new FilePickerFileType("Ảnh") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } },
                new FilePickerFileType("Văn bản / bảng") { Patterns = new[] { "*.txt", "*.md", "*.csv" } }
            }
        });

        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            if (!_files.Contains(path)) _files.Add(path);
        }

        RefreshFileList();
    }

    /// <summary>Mã của mọi công cụ có trong bảng, theo đúng thứ tự hiển thị.</summary>
    public static IReadOnlyList<string> ToolIds => Tools.Select(t => t.Id).ToList();

    /// <summary>Mở thẳng một công cụ theo mã. Trả về false nếu không có công cụ đó.</summary>
    public bool SelectToolById(string id)
    {
        var tool = Tools.FirstOrDefault(t => t.Id == id);
        if (tool == null) return false;

        SelectTool(tool);
        return true;
    }

    /// <summary>Số điều khiển đang hiển thị trong bảng tuỳ chọn của công cụ đang chọn.</summary>
    public int VisibleOptionCount => _workContent.Children.Count;

    /// <summary>Nạp sẵn một tệp vào danh sách đầu vào (dùng khi mở công cụ từ tài liệu đang xem).</summary>
    public void AddFile(string path)
    {
        if (!File.Exists(path) || _files.Contains(path)) return;
        _files.Add(path);
        RefreshFileList();
    }

    private void RefreshFileList()
    {
        _fileList.Children.Clear();

        if (_files.Count == 0)
        {
            _fileHint.Text = "Kéo thả tệp vào đây, hoặc bấm “Chọn tệp”. Hỗ trợ .pdf, .png, .jpg, .bmp, .txt, .csv";
            _fileHint.Foreground = Muted;
            return;
        }

        _fileHint.Text = $"Đã chọn {_files.Count} tệp — thứ tự dưới đây cũng là thứ tự khi gộp.";
        _fileHint.Foreground = Accent;

        for (int i = 0; i < _files.Count; i++)
        {
            int index = i;
            var path = _files[i];

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            var order = new TextBlock
            {
                Text = $"{i + 1}.",
                Width = 24,
                FontSize = 12,
                Foreground = Muted,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(order, 0);
            grid.Children.Add(order);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontSize = 12, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(new TextBlock
            {
                Text = FileSizeText(path),
                FontSize = 10,
                Foreground = Muted
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var up = SmallButton("↑");
            var down = SmallButton("↓");
            var remove = SmallButton("✕");

            up.Click += (s, e) => MoveFile(index, -1);
            down.Click += (s, e) => MoveFile(index, +1);
            remove.Click += (s, e) =>
            {
                _files.RemoveAt(index);
                RefreshFileList();
            };

            var actions = Row(up, down, remove);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            _fileList.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#FAFAFB")),
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6),
                Child = grid
            });
        }
    }

    private static Button SmallButton(string text) => new()
    {
        Content = text,
        Width = 26,
        Height = 24,
        Padding = new Thickness(0),
        FontSize = 12,
        Background = Brushes.White,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4)
    };

    private void MoveFile(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= _files.Count) return;

        (_files[index], _files[target]) = (_files[target], _files[index]);
        RefreshFileList();
    }

    private static string FileSizeText(string path)
    {
        try
        {
            var length = new FileInfo(path).Length;
            return FormatSize(length) + "  •  " + Path.GetDirectoryName(path);
        }
        catch
        {
            return "(không đọc được tệp)";
        }
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };
}
