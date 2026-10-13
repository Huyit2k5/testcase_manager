using System.Reflection;
using PdfSharp.Fonts;

namespace Acme.TestCaseManagement.Reports;

/// <summary>
/// The font of the PDF reports. Noto Sans is kept inside the assembly (cut down to Latin and Vietnamese), so the file looks the same on
/// any server and has the letters of Vietnamese whether or not the server has fonts of its own.
/// <para>
/// PDFsharp reads its fonts through one resolver for the whole process. This one answers for its own family only, and passes every other
/// family to the resolver that was set before it, so a host that draws PDFs of its own with PDFsharp is not disturbed.
/// </para>
/// </summary>
internal sealed class ReportFonts : IFontResolver
{
    public const string Family = "Noto Sans";

    private static readonly object Gate = new();
    private static bool _installed;

    private readonly IFontResolver? _previous;

    private ReportFonts(IFontResolver? previous)
    {
        _previous = previous;
    }

    /// <summary>Makes the font known to PDFsharp; does nothing the second time.</summary>
    public static void Install()
    {
        lock (Gate)
        {
            if (_installed && GlobalFontSettings.FontResolver is ReportFonts)
            {
                return;
            }

            GlobalFontSettings.FontResolver = new ReportFonts(GlobalFontSettings.FontResolver);
            _installed = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // MigraDoc names Courier New for the text it writes when something is wrong with the document; the one font stands in for it.
        if (string.Equals(familyName, Family, StringComparison.OrdinalIgnoreCase) || string.Equals(familyName, "Courier New", StringComparison.OrdinalIgnoreCase))
        {
            // There is no italic face: the regular one stands in for it.
            return new FontResolverInfo(isBold ? "NotoSans-Bold" : "NotoSans-Regular");
        }

        return _previous?.ResolveTypeface(familyName, isBold, isItalic);
    }

    public byte[]? GetFont(string faceName)
    {
        if (faceName is "NotoSans-Regular" or "NotoSans-Bold")
        {
            var resource = typeof(ReportFonts).Assembly.GetManifestResourceNames().Single(n => n.EndsWith(faceName + ".ttf", StringComparison.Ordinal));
            using var stream = typeof(ReportFonts).Assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        return _previous?.GetFont(faceName);
    }
}
