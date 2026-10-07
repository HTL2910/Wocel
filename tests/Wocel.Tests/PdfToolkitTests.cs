using System.Text;
using Wocel.Core.Pdf;
using Wocel.Core.Services;
using Xunit;

namespace Wocel.Tests;

public class PdfToolkitTests
{
    private const string Vietnamese = "Xin chào! Đây là tài liệu thử nghiệm của Wocel — có dấu tiếng Việt đầy đủ.";

    private static byte[] MakeDocument(string text, string title = "Tài liệu thử", int repeatLines = 1)
    {
        var body = new StringBuilder();
        for (int i = 1; i <= repeatLines; i++) body.AppendLine($"{i}. {text}");

        return PdfToolkit.TextToPdf(body.ToString(), new PdfTextToPdfOptions { Title = title });
    }

    private static byte[] MakePng(int width, int height)
    {
        var samples = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                samples[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                samples[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                samples[i + 2] = 128;
            }
        }

        return PdfImageCodec.EncodePng(samples, width, height, 3, 8);
    }

    // ── Nền tảng: đọc/ghi ────────────────────────────────────────────────
    [Fact]
    public void TextToPdf_ProducesLoadableDocument()
    {
        var bytes = MakeDocument(Vietnamese);

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(bytes[^32..]));

        var document = PdfToolkit.Open(bytes);
        Assert.Equal(PdfLoadStatus.Ok, document.Status);
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void TextToPdf_RoundTripsVietnameseText()
    {
        var bytes = MakeDocument(Vietnamese);
        var extracted = PdfToolkit.ExtractText(bytes);

        // Chỉ kiểm tra khi máy chạy test có font Unicode để nhúng.
        if (PdfTrueTypeFont.FindSystemFont() != null)
            Assert.Contains("tiếng Việt", extracted);
        else
            Assert.Contains("Viet", extracted);
    }

    [Fact]
    public void TextToPdf_PaginatesLongContent()
    {
        var bytes = MakeDocument(Vietnamese, repeatLines: 200);
        var document = PdfToolkit.Open(bytes);
        Assert.True(document.PageCount > 1, $"Cần nhiều hơn 1 trang, thực tế {document.PageCount}");
    }

    // ── Thao tác trang ───────────────────────────────────────────────────
    [Fact]
    public void Merge_CombinesAllPages()
    {
        var first = MakeDocument("Tệp thứ nhất", repeatLines: 60);
        var second = MakeDocument("Tệp thứ hai", repeatLines: 60);

        int expected = PdfToolkit.Open(first).PageCount + PdfToolkit.Open(second).PageCount;
        var merged = PdfToolkit.Merge(new[] { first, second });

        Assert.Equal(expected, PdfToolkit.Open(merged).PageCount);
        var text = PdfToolkit.ExtractText(merged);
        Assert.Contains("nh", text);
    }

    [Fact]
    public void Merge_RequiresTwoFiles()
    {
        var single = MakeDocument("một tệp");
        Assert.Throws<PdfToolException>(() => PdfToolkit.Merge(new[] { single }));
    }

    [Fact]
    public void Split_EveryPage_ProducesOneFilePerPage()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 150);
        int pageCount = PdfToolkit.Open(source).PageCount;

        var parts = PdfToolkit.Split(source, new PdfSplitOptions { Mode = PdfSplitMode.EveryPage });

        Assert.Equal(pageCount, parts.Count);
        Assert.All(parts, part => Assert.Equal(1, PdfToolkit.Open(part.Data).PageCount));
    }

    [Fact]
    public void Split_EveryNPages_GroupsCorrectly()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 150);
        int pageCount = PdfToolkit.Open(source).PageCount;

        var parts = PdfToolkit.Split(source, new PdfSplitOptions
        {
            Mode = PdfSplitMode.EveryNPages,
            PagesPerFile = 2
        });

        Assert.Equal((int)Math.Ceiling(pageCount / 2.0), parts.Count);
        Assert.Equal(pageCount, parts.Sum(p => PdfToolkit.Open(p.Data).PageCount));
    }

    [Fact]
    public void ExtractPages_KeepsOnlySelected()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 200);
        int pageCount = PdfToolkit.Open(source).PageCount;
        Assert.True(pageCount >= 3);

        var extracted = PdfToolkit.ExtractPages(source, "1,3");
        Assert.Equal(2, PdfToolkit.Open(extracted).PageCount);
    }

    [Fact]
    public void RemovePages_DropsSelected()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 200);
        int pageCount = PdfToolkit.Open(source).PageCount;

        var trimmed = PdfToolkit.RemovePages(source, "1");
        Assert.Equal(pageCount - 1, PdfToolkit.Open(trimmed).PageCount);
    }

    [Fact]
    public void RemovePages_RefusesToEmptyDocument()
    {
        var source = MakeDocument(Vietnamese);
        Assert.Throws<PdfToolException>(() => PdfToolkit.RemovePages(source, "1-999"));
    }

    [Fact]
    public void ReversePages_InvertsOrder()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 200);
        var original = PdfToolkit.ExtractTextPerPage(source);
        Assert.True(original.Count >= 2);

        var reversed = PdfToolkit.ExtractTextPerPage(PdfToolkit.ReversePages(source));
        Assert.Equal(original.Count, reversed.Count);
        Assert.Equal(original[0].Trim(), reversed[^1].Trim());
    }

    [Fact]
    public void RotatePages_SetsRotationAndSwapsDisplaySize()
    {
        var source = MakeDocument(Vietnamese);
        var before = PdfToolkit.Open(source).Pages[0];
        double beforeWidth = before.DisplayWidth;

        var rotated = PdfToolkit.RotatePages(source, 90);
        var page = PdfToolkit.Open(rotated).Pages[0];

        Assert.Equal(90, page.Rotate);
        Assert.Equal(beforeWidth, page.DisplayHeight, 1);
    }

    [Fact]
    public void ResizePages_ChangesPageBox()
    {
        var source = MakeDocument(Vietnamese);
        var resized = PdfToolkit.ResizePages(source, PdfPageSizes.A5);
        var page = PdfToolkit.Open(resized).Pages[0];

        Assert.Equal(PdfPageSizes.A5[2], page.WidthPoints, 1);
        Assert.Equal(PdfPageSizes.A5[3], page.HeightPoints, 1);
    }

    [Fact]
    public void CropPages_ShrinksBox()
    {
        var source = MakeDocument(Vietnamese);
        double originalWidth = PdfToolkit.Open(source).Pages[0].WidthPoints;

        var cropped = PdfToolkit.CropPages(source, 10, 10, 10, 10);
        var page = PdfToolkit.Open(cropped).Pages[0];

        Assert.True(page.WidthPoints < originalWidth);
        Assert.Equal(originalWidth * 0.8, page.WidthPoints, 1);
    }

    [Fact]
    public void NUp_PacksPagesOntoFewerSheets()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 200);
        int pageCount = PdfToolkit.Open(source).PageCount;

        var packed = PdfToolkit.NUp(source, 2);
        Assert.Equal((int)Math.Ceiling(pageCount / 2.0), PdfToolkit.Open(packed).PageCount);
    }

    // ── Nội dung ─────────────────────────────────────────────────────────
    [Fact]
    public void AddWatermark_AddsVisibleText()
    {
        var source = MakeDocument(Vietnamese);
        var stamped = PdfToolkit.AddWatermark(source, new PdfWatermarkOptions { Text = "BAN NHAP" });

        var text = PdfToolkit.ExtractText(stamped);
        Assert.Contains("BAN NHAP", text);
        Assert.Equal(PdfToolkit.Open(source).PageCount, PdfToolkit.Open(stamped).PageCount);
    }

    [Fact]
    public void AddPageNumbers_NumbersEveryPage()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 150);
        var numbered = PdfToolkit.AddPageNumbers(source, new PdfPageNumberOptions { Format = "Trang {0}/{1}" });

        var pages = PdfToolkit.ExtractTextPerPage(numbered);
        Assert.Contains($"Trang 1/{pages.Count}", pages[0]);
        Assert.Contains($"Trang 2/{pages.Count}", pages[1]);
    }

    [Fact]
    public void AddHeaderFooter_ExpandsPlaceholders()
    {
        var source = MakeDocument(Vietnamese);
        var result = PdfToolkit.AddHeaderFooter(source, new PdfHeaderFooterOptions
        {
            HeaderLeft = "Wocel",
            FooterRight = "{page}/{pages}"
        });

        var text = PdfToolkit.ExtractText(result);
        Assert.Contains("Wocel", text);
        Assert.Contains("1/1", text);
    }

    [Fact]
    public void ReplaceText_SwapsContent()
    {
        var source = PdfToolkit.TextToPdf("Hop dong so ABC123 ky ngay hom nay.");
        var (data, count) = PdfToolkit.ReplaceText(source, "ABC123", "XYZ789");

        Assert.True(count > 0);
        var text = PdfToolkit.ExtractText(data);
        Assert.Contains("XYZ789", text);
        Assert.DoesNotContain("ABC123", text);
    }

    [Fact]
    public void Redact_RemovesTextFromContent()
    {
        var source = PdfToolkit.TextToPdf("So tai khoan 0123456789 la thong tin mat.");
        var (data, count) = PdfToolkit.Redact(source, new[] { "0123456789" });

        Assert.True(count > 0);
        Assert.DoesNotContain("0123456789", PdfToolkit.ExtractText(data));
    }

    // ── Tài liệu ─────────────────────────────────────────────────────────
    [Fact]
    public void Metadata_RoundTrips()
    {
        var source = MakeDocument(Vietnamese);
        var updated = PdfToolkit.SetMetadata(source, new PdfMetadata
        {
            Title = "Báo cáo quý IV",
            Author = "Nguyễn Văn A",
            Keywords = "wocel, pdf, báo cáo"
        });

        var metadata = PdfToolkit.GetMetadata(updated);
        Assert.Equal("Báo cáo quý IV", metadata.Title);
        Assert.Equal("Nguyễn Văn A", metadata.Author);
        Assert.Equal("wocel, pdf, báo cáo", metadata.Keywords);
    }

    [Fact]
    public void Inspect_ReportsStructure()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 120);
        var summary = PdfToolkit.Inspect(source, "thu.pdf");

        Assert.Equal("thu.pdf", summary.FileName);
        Assert.True(summary.PageCount >= 1);
        Assert.False(summary.IsEncrypted);
        Assert.True(summary.CharacterCount > 0);
        Assert.True(summary.FontCount >= 1);
    }

    [Fact]
    public void Compress_KeepsDocumentReadable()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 300);
        var result = PdfToolkit.Compress(source, PdfCompressionLevel.Balanced);

        Assert.True(result.NewSize > 0);
        var document = PdfToolkit.Open(result.Data);
        Assert.Equal(PdfToolkit.Open(source).PageCount, document.PageCount);
    }

    [Fact]
    public void Repair_RecoversDocumentWithBrokenXref()
    {
        var source = MakeDocument(Vietnamese, repeatLines: 20);

        // Phá hỏng offset trong startxref như khi tệp bị cắt cụt/truyền lỗi.
        var text = Encoding.Latin1.GetString(source);
        int index = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var broken = Encoding.Latin1.GetBytes(text[..(index + 10)] + "999999999\n%%EOF\n");

        var (data, report) = PdfToolkit.Repair(broken);
        Assert.NotEmpty(report);
        Assert.Equal(PdfToolkit.Open(source).PageCount, PdfToolkit.Open(data).PageCount);
    }

    [Fact]
    public void Compare_DetectsDifferences()
    {
        var left = PdfToolkit.TextToPdf("Dong mot\nDong hai\nDong ba");
        var right = PdfToolkit.TextToPdf("Dong mot\nDong hai da sua\nDong ba");

        var result = PdfToolkit.Compare(left, right);
        Assert.False(result.Identical);
        Assert.True(result.ChangedPages >= 1);
    }

    [Fact]
    public void Compare_ReportsIdenticalFiles()
    {
        var content = "Noi dung giong het nhau";
        var result = PdfToolkit.Compare(PdfToolkit.TextToPdf(content), PdfToolkit.TextToPdf(content));
        Assert.True(result.Identical);
    }

    // ── Bảo mật ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Rc4128)]
    [InlineData(PdfEncryptionAlgorithm.Aes128)]
    [InlineData(PdfEncryptionAlgorithm.Aes256)]
    public void Protect_ThenUnlock_RoundTrips(PdfEncryptionAlgorithm algorithm)
    {
        var source = PdfToolkit.TextToPdf("Tai lieu can bao mat");
        var locked = PdfToolkit.Protect(source, new PdfEncryptionSettings
        {
            UserPassword = "mat-khau-123",
            OwnerPassword = "chu-so-huu",
            Algorithm = algorithm,
            Permissions = PdfPermissions.Print
        });

        Assert.Throws<PdfToolException>(() => PdfToolkit.Open(locked));
        Assert.Throws<PdfToolException>(() => PdfToolkit.Open(locked, "sai-mat-khau"));

        var unlocked = PdfToolkit.Unlock(locked, "mat-khau-123");
        Assert.Contains("bao mat", PdfToolkit.ExtractText(unlocked));

        var withOwner = PdfToolkit.Unlock(locked, "chu-so-huu");
        Assert.Equal(1, PdfToolkit.Open(withOwner).PageCount);
    }

    [Fact]
    public void Unlock_RequiresPassword()
    {
        var source = PdfToolkit.TextToPdf("noi dung");
        Assert.Throws<PdfToolException>(() => PdfToolkit.Unlock(source, string.Empty));
    }

    // ── Ảnh ──────────────────────────────────────────────────────────────
    [Fact]
    public void ImagesToPdf_CreatesOnePagePerImage()
    {
        var images = new[]
        {
            ("anh1.png", MakePng(120, 90)),
            ("anh2.png", MakePng(90, 120))
        };

        var pdf = PdfToolkit.ImagesToPdf(images);
        Assert.Equal(2, PdfToolkit.Open(pdf).PageCount);
    }

    [Fact]
    public void ExtractImages_FindsEmbeddedImages()
    {
        var pdf = PdfToolkit.ImagesToPdf(new[] { ("anh.png", MakePng(64, 48)) });
        var extracted = PdfToolkit.ExtractImages(pdf);

        Assert.Single(extracted);
        Assert.Equal(64, extracted[0].Width);
        Assert.Equal(48, extracted[0].Height);
        Assert.Equal(".png", extracted[0].Extension);
    }

    [Fact]
    public void PngRoundTrip_PreservesPixels()
    {
        var png = MakePng(16, 8);
        var image = PdfImageCodec.ReadImageFile(png, ".png");

        Assert.Equal(16, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Equal(3, image.Channels);
        Assert.Equal(16 * 8 * 3, image.Samples.Length);
        Assert.Equal(0, image.Samples[0]);
        Assert.Equal(255, image.Samples[15 * 3]);
    }

    // ── Bảng ─────────────────────────────────────────────────────────────
    [Fact]
    public void TableToPdf_RendersAllRows()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Mã", "Tên hàng", "Số lượng" },
            new[] { "SP001", "Bàn phím cơ", "12" },
            new[] { "SP002", "Chuột không dây", "34" }
        };

        var pdf = PdfToolkit.TableToPdf(rows, new PdfTableToPdfOptions { Title = "Bảng kê" });
        var text = PdfToolkit.ExtractText(pdf);

        Assert.Contains("SP001", text);
        Assert.Contains("SP002", text);
    }

    [Fact]
    public void ExtractTables_ReadsBackColumns()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Ma", "Ten", "SL" },
            new[] { "SP001", "Ban phim", "12" }
        };

        var pdf = PdfToolkit.TableToPdf(rows);
        var parsed = PdfToolkit.ExtractTables(pdf);

        Assert.Contains(parsed, row => row.Any(c => c.Contains("SP001")));
        Assert.Contains(parsed, row => row.Count >= 2);
    }

    [Fact]
    public void Csv_RoundTripsQuotedValues()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "a", "b,c", "d\"e" }
        };

        var csv = PdfToolkit.ToCsv(rows);
        var parsed = PdfToolkit.ParseCsv(csv);

        Assert.Single(parsed);
        Assert.Equal(new[] { "a", "b,c", "d\"e" }, parsed[0]);
    }

    // ── Chọn trang ───────────────────────────────────────────────────────
    [Theory]
    [InlineData("1,3,5", new[] { 1, 3, 5 })]
    [InlineData("2-4", new[] { 2, 3, 4 })]
    [InlineData("1, 3-4, 8", new[] { 1, 3, 4, 8 })]
    [InlineData("8-", new[] { 8, 9, 10 })]
    [InlineData("chẵn", new[] { 2, 4, 6, 8, 10 })]
    [InlineData("odd", new[] { 1, 3, 5, 7, 9 })]
    [InlineData("99", new int[0])]
    public void PageRange_ParsesUserInput(string expression, int[] expected)
    {
        Assert.Equal(expected, PdfPageRange.Parse(expression, 10));
    }

    [Fact]
    public void PageRange_DescribesCompactly()
    {
        Assert.Equal("1-3, 7, 10-12", PdfPageRange.Describe(new[] { 1, 2, 3, 7, 10, 11, 12 }));
    }

    // ── Lỗi thân thiện ───────────────────────────────────────────────────
    [Fact]
    public void Open_RejectsEmptyInput()
    {
        var error = Assert.Throws<PdfToolException>(() => PdfToolkit.Open(Array.Empty<byte>()));
        Assert.Contains("rỗng", error.Message);
    }

    [Fact]
    public void Open_RejectsNonPdfContent()
    {
        var junk = Encoding.UTF8.GetBytes(new string('x', 5000));
        Assert.Throws<PdfToolException>(() => PdfToolkit.Open(junk));
    }
}
