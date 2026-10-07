using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Wocel.Excel.Engine;

namespace Wocel.Shell.Controls;

/// <summary>
/// Ô gợi ý công thức kiểu Excel: gõ "=SU" thì hiện danh sách SUM, SUMIF…
/// Gắn được vào bất kỳ TextBox nào (ô nhập trong lưới hoặc thanh công thức).
/// </summary>
public sealed class FormulaSuggestionPopup
{
    private const int MaxVisible = 8;

    private readonly TextBox _target;
    private readonly Popup _popup;
    private readonly StackPanel _rows;
    private readonly TextBlock _footer;

    private List<FormulaFunctionInfo> _matches = new();
    private int _selectedIndex;
    private string _prefix = string.Empty;
    private bool _suppressRefresh;

    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#111827"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#107C41"));
    private static readonly IBrush Highlight = new SolidColorBrush(Color.Parse("#EAF6EF"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#D8DBE0"));

    public bool IsOpen => _popup.IsOpen;
    public IReadOnlyList<FormulaFunctionInfo> Suggestions => _matches;
    public int SelectedIndex => _selectedIndex;
    public FormulaFunctionInfo? SelectedFunction =>
        _selectedIndex >= 0 && _selectedIndex < _matches.Count ? _matches[_selectedIndex] : null;

    public static FormulaSuggestionPopup Attach(TextBox target) => new(target);

    private FormulaSuggestionPopup(TextBox target)
    {
        _target = target;

        _rows = new StackPanel();
        _footer = new TextBlock
        {
            FontSize = 10.5,
            Foreground = Muted,
            Margin = new Thickness(10, 6),
            Text = "↑ ↓ chọn · Tab hoặc Enter để chèn · Esc để bỏ qua"
        };

        var content = new StackPanel();
        content.Children.Add(_rows);
        content.Children.Add(new Border { Height = 1, Background = Line });
        content.Children.Add(_footer);

        _popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = false,
            Focusable = false,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                MinWidth = 340,
                BoxShadow = BoxShadows.Parse("0 6 18 0 #22000000"),
                Child = content
            }
        };

        // Popup tạo bằng code phải được gắn cha logic thì mới tìm được cửa sổ chứa nó.
        ((ISetLogicalParent)_popup).SetParent(target);

        _target.PropertyChanged += OnTargetPropertyChanged;
        _target.LostFocus += (_, _) => Close();

        // Bắt phím ở pha tunnel để giành quyền xử lý trước TextBox và trước
        // các handler commit ô của lưới.
        _target.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_suppressRefresh) return;
        if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty) Refresh();
        else if (e.Property == Visual.IsVisibleProperty && !_target.IsVisible) Close();
    }

    /// <summary>Tính lại danh sách gợi ý theo nội dung đang gõ.</summary>
    public void Refresh()
    {
        var prefix = FormulaFunctionCatalog.GetPrefixAtCaret(_target.Text, _target.CaretIndex);
        if (prefix == null)
        {
            Close();
            return;
        }

        var matches = FormulaFunctionCatalog.Search(prefix, MaxVisible);
        if (matches.Count == 0)
        {
            Close();
            return;
        }

        _prefix = prefix;
        _matches = matches;
        _selectedIndex = 0;
        RenderRows();
        _popup.IsOpen = true;
    }

    private void RenderRows()
    {
        _rows.Children.Clear();

        for (int i = 0; i < _matches.Count; i++)
        {
            int index = i;
            var function = _matches[i];
            bool active = i == _selectedIndex;

            var name = new TextBlock
            {
                Text = function.Name,
                FontWeight = FontWeight.Bold,
                FontSize = 12.5,
                Foreground = active ? Accent : Ink,
                Width = 108,
                VerticalAlignment = VerticalAlignment.Center
            };

            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            detail.Children.Add(new TextBlock { Text = function.Signature, FontSize = 11.5, Foreground = Ink });
            detail.Children.Add(new TextBlock { Text = function.Description, FontSize = 10.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap });

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("108,*") };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(detail, 1);
            grid.Children.Add(name);
            grid.Children.Add(detail);

            var row = new Border
            {
                Background = active ? Highlight : Brushes.Transparent,
                Padding = new Thickness(10, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = grid
            };

            row.PointerPressed += (_, e) =>
            {
                _selectedIndex = index;
                Accept();
                e.Handled = true;
            };

            _rows.Children.Add(row);
        }

        _footer.Text = _matches.Count == 1
            ? $"{_matches[0].Name} — {_matches[0].Category}  ·  Tab hoặc Enter để chèn"
            : $"{_matches.Count} hàm khớp “{_prefix}”  ·  ↑ ↓ chọn · Tab hoặc Enter để chèn · Esc bỏ qua";
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Tab:
                Accept();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    public void Move(int delta)
    {
        if (_matches.Count == 0) return;

        _selectedIndex = (_selectedIndex + delta + _matches.Count) % _matches.Count;
        RenderRows();
    }

    /// <summary>Chèn hàm đang chọn vào ô nhập, thay cho phần tên đang gõ dở.</summary>
    public void Accept()
    {
        var function = SelectedFunction;
        if (function == null)
        {
            Close();
            return;
        }

        var text = _target.Text ?? string.Empty;
        int caret = Math.Clamp(_target.CaretIndex, 0, text.Length);
        int start = Math.Max(0, caret - _prefix.Length);

        var replacement = function.Name + "(";
        var updated = text[..start] + replacement + text[caret..];

        _suppressRefresh = true;
        _target.Text = updated;
        _target.CaretIndex = start + replacement.Length;
        _suppressRefresh = false;

        Close();
    }

    public void Close()
    {
        _popup.IsOpen = false;
        _matches = new List<FormulaFunctionInfo>();
        _selectedIndex = 0;
    }

    /// <summary>Gỡ khỏi TextBox khi không cần nữa.</summary>
    public void Detach()
    {
        Close();
        _target.PropertyChanged -= OnTargetPropertyChanged;
        _target.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKeyDown);
    }
}
