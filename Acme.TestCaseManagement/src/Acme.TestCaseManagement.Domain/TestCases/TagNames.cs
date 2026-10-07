using Volo.Abp;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>The rules for a tag, in one place so that the screen, the API and the import agree.</summary>
public static class TagNames
{
    /// <summary>Several tags in one cell of a file are separated by this.</summary>
    public const char FileSeparator = ';';

    /// <summary>A tag as it is compared: lower case, with the spaces inside collapsed to one.</summary>
    public static string Normalize(string name) => Clean(name).ToLowerInvariant();

    /// <summary>The text of a tag with the ends trimmed and the runs of white space inside made one space.</summary>
    public static string Clean(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Whether a tag (after cleaning) may be used; the reason is given when it may not.</summary>
    public static bool IsValid(string cleaned) =>
        cleaned.Length is > 0 and <= TagConsts.MaxLength && !cleaned.Any(c => char.IsControl(c) || c == ',' || c == FileSeparator);

    /// <summary>
    /// The tags of a list, ready to keep: cleaned, empty ones dropped, doubles (ignoring case) dropped with the first spelling
    /// kept. A tag that may not be used, or more than the limit, is a <see cref="BusinessException"/>.
    /// </summary>
    public static List<string> Prepare(IEnumerable<string?>? names)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();

        foreach (var raw in names ?? Enumerable.Empty<string?>())
        {
            var cleaned = Clean(raw ?? string.Empty);
            if (cleaned.Length == 0)
            {
                continue;
            }

            if (!IsValid(cleaned))
            {
                throw new BusinessException(TestCaseManagementErrorCodes.InvalidTag)
                    .WithData("Tag", cleaned.Length > 60 ? cleaned[..60] + "..." : cleaned)
                    .WithData("Max", TagConsts.MaxLength);
            }

            if (seen.Add(cleaned.ToLowerInvariant()))
            {
                result.Add(cleaned);
            }
        }

        if (result.Count > TagConsts.MaxPerTestCase)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TooManyTags)
                .WithData("Count", result.Count)
                .WithData("Max", TagConsts.MaxPerTestCase);
        }

        return result;
    }

    /// <summary>Splits the cell of a file ("smoke; payments") into tags; whether they are usable is for <see cref="Prepare"/>.</summary>
    public static List<string> Split(string? cell) =>
        (cell ?? string.Empty).Split(FileSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
