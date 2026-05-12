using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Gateway.Controllers;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Gateway.Services.Reporting;

/// <summary>
/// Plan #47 — emits a single-page A4-landscape DOCX containing the
/// school's administrative-structure chart as an embedded PNG rendered by
/// <see cref="OrgStructureImageRenderer"/>. This gives pixel-perfect
/// fidelity to the obec reference template — director + dotted-line board,
/// vertical trunk through deputies, 4 division columns with stacked
/// numbered task boxes.
///
/// (The earlier table-based DOCX is replaced: tables can't draw the
/// connector lines that make this a real org chart.)
/// </summary>
public sealed class OrgStructureDocxGenerator
{
    private readonly OrgStructureImageRenderer _imageRenderer;

    public OrgStructureDocxGenerator(OrgStructureImageRenderer imageRenderer)
    {
        _imageRenderer = imageRenderer;
    }

    public MemoryStream Generate(OrgStructureDto data)
    {
        var pngBytes = _imageRenderer.RenderPng(data);

        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            body.AppendChild(BuildSectionProperties());

            // Embed the PNG as an image part referenced from a Drawing run.
            var imagePart = main.AddImagePart(ImagePartType.Png);
            using (var stream = new MemoryStream(pngBytes))
            {
                imagePart.FeedData(stream);
            }
            var relId = main.GetIdOfPart(imagePart);

            // Canvas is 1700×1100 px. Fit to A4 landscape printable area
            // (page 297mm × 210mm minus 10mm margins → ~277mm wide).
            // 1 inch = 914400 EMU → 10.9 in × 7.05 in
            const long widthEmu = 9982200L;   // 10.92 in (~277mm)
            const long heightEmu = 6459400L;  // 7.06 in (preserves 1700:1100 aspect)

            var paragraph = new Paragraph(new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { After = "0", Before = "0" }));
            paragraph.AppendChild(BuildPicRun(relId, widthEmu, heightEmu));
            body.AppendChild(paragraph);

            main.Document.Save();
        }
        ms.Position = 0;
        return ms;
    }

    private static SectionProperties BuildSectionProperties() => new(
        new PageSize { Width = 16838u, Height = 11906u, Orient = PageOrientationValues.Landscape },
        new PageMargin { Top = 567, Right = 567, Bottom = 567, Left = 567, Header = 0, Footer = 0 }
    );

    private static Run BuildPicRun(string relationshipId, long widthEmu, long heightEmu)
    {
        var picId = (uint)Random.Shared.Next(1, 100000);
        var run = new Run();
        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = picId, Name = $"OrgStructureChart-{picId}" },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = (uint)0, Name = $"org-{picId}.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip(
                                    new A.BlipExtensionList(
                                        new A.BlipExtension { Uri = "{28A0092B-C50C-407E-A947-70E740481C1C}" }))
                                {
                                    Embed = relationshipId,
                                    CompressionState = A.BlipCompressionValues.Print,
                                },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
                    }))
            {
                DistanceFromTop = (UInt32Value)0U,
                DistanceFromBottom = (UInt32Value)0U,
                DistanceFromLeft = (UInt32Value)0U,
                DistanceFromRight = (UInt32Value)0U,
                EditId = "50D07946",
            });
        run.AppendChild(drawing);
        return run;
    }
}
