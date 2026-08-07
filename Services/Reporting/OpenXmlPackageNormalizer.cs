using System.IO.Compression;

namespace Gateway.Services.Reporting;

/// <summary>
/// Port of BudgetApi's DocxPackageNormalizer (feedback id=41), widened to
/// every OpenXml package Gateway produces.
///
/// <para>DocumentFormat.OpenXml 3.2.0 writes the package's root relationship
/// with an <em>absolute</em> target — <c>Target="/word/document.xml"</c> — for
/// every document created via <c>WordprocessingDocument.Create</c>. Word
/// tolerates it; macOS Quick Look/Pages, Google Docs and mobile viewers reject
/// the whole package as "not in the correct format".</para>
///
/// <para>Gateway's org-structure report was built the same way and had the same
/// defect — confirmed against production on 2026-08-06: the downloaded package
/// contained <c>Target="/word/document.xml"</c> and no relative form. Nobody had
/// reported it, which fits: the people who would try it open .docx in Word.</para>
///
/// <para>Duplicated rather than shared because Gateway consumes SBD.* through
/// NuGet while BudgetApi is a separate bounded context — moving it into a shared
/// package would couple two services over eight lines of string replacement.
/// If a third service needs it, promote it to SBD.Infrastructure then.</para>
/// </summary>
public static class OpenXmlPackageNormalizer
{
    private const string RootRels = "_rels/.rels";

    public static Stream Normalize(Stream package)
    {
        var buffer = new MemoryStream();
        package.Position = 0;
        package.CopyTo(buffer);

        string original;
        buffer.Position = 0;
        using (var probe = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = probe.GetEntry(RootRels);
            if (entry is null)
            {
                buffer.Position = 0;
                return buffer;
            }
            using var reader = new StreamReader(entry.Open());
            original = reader.ReadToEnd();
        }

        var repaired = original.Replace("Target=\"/", "Target=\"");
        if (repaired == original)
        {
            buffer.Position = 0;
            return buffer;
        }

        buffer.Position = 0;
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(RootRels)!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(RootRels).Open());
            writer.Write(repaired);
        }

        buffer.Position = 0;
        return buffer;
    }
}
