using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Wocel.Core.Models;

namespace Wocel.Word.IO;

public static class DocxWriter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PKG = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static void Write(DocumentDocument doc, Stream outputStream)
    {
        using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);

        // 1. [Content_Types].xml
        var contentTypesEntry = archive.CreateEntry("[Content_Types].xml");
        using (var writer = new StreamWriter(contentTypesEntry.Open(), Encoding.UTF8))
        {
            var contentTypesXml = new XDocument(
                new XElement(PKG + "Types",
                    new XElement(PKG + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(PKG + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(PKG + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))
                )
            );
            contentTypesXml.Save(writer);
        }

        // 2. _rels/.rels
        var rootRelsEntry = archive.CreateEntry("_rels/.rels");
        using (var writer = new StreamWriter(rootRelsEntry.Open(), Encoding.UTF8))
        {
            var rootRelsXml = new XDocument(
                new XElement(R + "Relationships",
                    new XElement(R + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "word/document.xml"))
                )
            );
            rootRelsXml.Save(writer);
        }

        // 3. word/document.xml
        var bodyElem = new XElement(W + "body");

        foreach (var block in doc.Blocks)
        {
            if (block.Type == BlockType.Table && block.TableData != null)
            {
                var tbl = new XElement(W + "tbl");
                foreach (var row in block.TableData)
                {
                    var tr = new XElement(W + "tr");
                    foreach (var cell in row)
                    {
                        var tc = new XElement(W + "tc",
                            new XElement(W + "p",
                                new XElement(W + "r",
                                    new XElement(W + "t", cell ?? string.Empty))));
                        tr.Add(tc);
                    }
                    tbl.Add(tr);
                }
                bodyElem.Add(tbl);
            }
            else
            {
                var p = new XElement(W + "p");
                
                // Add heading styling if needed
                if (block.Type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
                {
                    var styleName = block.Type.ToString();
                    p.Add(new XElement(W + "pPr",
                        new XElement(W + "pStyle", new XAttribute(W + "val", styleName))));
                }

                foreach (var inline in block.Inlines)
                {
                    var rPr = new XElement(W + "rPr");

                    if (inline.FontFamily is { Length: > 0 } fontFamily)
                    {
                        rPr.Add(new XElement(W + "rFonts",
                            new XAttribute(W + "ascii", fontFamily),
                            new XAttribute(W + "hAnsi", fontFamily)));
                    }

                    if (inline.IsBold) rPr.Add(new XElement(W + "b"));
                    if (inline.IsItalic) rPr.Add(new XElement(W + "i"));
                    if (inline.IsUnderline) rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));

                    // Word đo cỡ chữ theo nửa point, nên phải nhân đôi.
                    if (inline.FontSize is > 0)
                        rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", inline.FontSize.Value * 2)));

                    if (inline.FontColor is { Length: > 0 } color)
                        rPr.Add(new XElement(W + "color", new XAttribute(W + "val", color.TrimStart('#'))));

                    var r = new XElement(W + "r");
                    if (rPr.HasElements) r.Add(rPr);
                    r.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), inline.Text));

                    p.Add(r);
                }

                bodyElem.Add(p);
            }
        }

        var docEntry = archive.CreateEntry("word/document.xml");
        using (var writer = new StreamWriter(docEntry.Open(), Encoding.UTF8))
        {
            var docXml = new XDocument(
                new XElement(W + "document",
                    new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                    bodyElem
                )
            );
            docXml.Save(writer);
        }
    }
}
