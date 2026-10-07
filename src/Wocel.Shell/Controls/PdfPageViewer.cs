using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Wocel.Shell.Controls;

/// <summary>
/// Lightweight PDF page viewer — renders extracted text pages as A4 "paper" panels.
/// Real pixel-accurate PDF rendering requires a native lib; this shows extracted text
/// with correct typography and pagination controls.
/// </summary>
public class PdfPageViewer : Control
{
    private static readonly Typeface PageTypeface = new("Calibri, Segoe UI, -apple-system, sans-serif");
    private static readonly IBrush PageBgBrush = Brushes.White;
    private static readonly IBrush PageShadowBrush = new SolidColorBrush(Color.Parse("#44000000"));
    private static readonly IBrush CanvasBgBrush = new SolidColorBrush(Color.Parse("#E5E5E5"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly Pen PageBorderPen = new(new SolidColorBrush(Color.Parse("#CCCCCC")), 1);

    private const double PageWidth = 680;
    private const double PageHeight = 960;
    private const double PageMargin = 30;
    private const double ContentPadding = 50;

    public static readonly StyledProperty<string?> PageTextProperty =
        AvaloniaProperty.Register<PdfPageViewer, string?>(nameof(PageText));

    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<PdfPageViewer, int>(nameof(CurrentPage), 1);

    public static readonly StyledProperty<int> TotalPagesProperty =
        AvaloniaProperty.Register<PdfPageViewer, int>(nameof(TotalPages), 1);

    public string? PageText
    {
        get => GetValue(PageTextProperty);
        set => SetValue(PageTextProperty, value);
    }

    public int CurrentPage
    {
        get => GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public int TotalPages
    {
        get => GetValue(TotalPagesProperty);
        set => SetValue(TotalPagesProperty, value);
    }

    static PdfPageViewer()
    {
        PageTextProperty.Changed.AddClassHandler<PdfPageViewer>((x, _) => x.InvalidateVisual());
        CurrentPageProperty.Changed.AddClassHandler<PdfPageViewer>((x, _) => x.InvalidateVisual());
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(PageWidth + PageMargin * 2, PageHeight + PageMargin * 2);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        var totalW = PageWidth + PageMargin * 2;
        var totalH = PageHeight + PageMargin * 2;

        // Canvas background
        context.FillRectangle(CanvasBgBrush, new Rect(0, 0, totalW, totalH));

        // Drop shadow
        var shadowRect = new Rect(PageMargin + 4, PageMargin + 4, PageWidth, PageHeight);
        context.FillRectangle(PageShadowBrush, shadowRect);

        // White A4 page
        var pageRect = new Rect(PageMargin, PageMargin, PageWidth, PageHeight);
        context.FillRectangle(PageBgBrush, pageRect);
        context.DrawRectangle(null, PageBorderPen, pageRect);

        // Page number indicator (top right)
        var pageNumText = $"Trang {CurrentPage} / {TotalPages}";
        var pageNumFt = new FormattedText(pageNumText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            PageTypeface, 10, new SolidColorBrush(Color.Parse("#999999")));
        context.DrawText(pageNumFt, new Point(PageMargin + PageWidth - pageNumFt.Width - 10, PageMargin + 10));

        // Empty state
        if (string.IsNullOrWhiteSpace(PageText))
        {
            var emptyFt = new FormattedText(
                "(Trang này không có nội dung văn bản)",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                PageTypeface, 14, new SolidColorBrush(Color.Parse("#AAAAAA")));
            context.DrawText(emptyFt, new Point(PageMargin + ContentPadding, PageMargin + PageHeight / 2));
            return;
        }

        // Draw text line by line, wrapping at content width
        var contentWidth = PageWidth - ContentPadding * 2;
        var lines = PageText.Split('\n');
        var y = PageMargin + ContentPadding;
        var maxY = PageMargin + PageHeight - ContentPadding;

        foreach (var rawLine in lines)
        {
            if (y > maxY) break;

            var line = rawLine.TrimEnd();
            if (string.IsNullOrEmpty(line))
            {
                y += 20; // Empty line spacing
                continue;
            }

            // Detect heading-like lines (ALL CAPS or starts with # or very short)
            bool isHeading = line.Length < 80 && line.ToUpperInvariant() == line && line.Length > 2;
            var fontSize = isHeading ? 14.0 : 12.0;
            var fontWeight = isHeading ? FontWeight.Bold : FontWeight.Normal;
            var lineTypeface = new Typeface("Calibri, Segoe UI, -apple-system, sans-serif",
                FontStyle.Normal, fontWeight);

            var ft = new FormattedText(line, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                lineTypeface, fontSize, TextBrush)
            {
                MaxTextWidth = contentWidth
            };

            context.DrawText(ft, new Point(PageMargin + ContentPadding, y));
            y += ft.Height + (isHeading ? 8 : 4);
        }
    }
}
