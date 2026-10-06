using System.Text.RegularExpressions;
using Acme.TestCaseManagement.Transfer.Tabular;
using Microsoft.Extensions.Localization;

namespace Acme.TestCaseManagement.Transfer;

/// <summary>The localized texts of an import report. Placeholders are named, as in the rest of the module's resource.</summary>
internal sealed partial class TransferMessages
{
    private readonly IStringLocalizer _localizer;

    public TransferMessages(IStringLocalizer localizer)
    {
        _localizer = localizer;
    }

    public string Get(string key, params (string Name, object? Value)[] values)
    {
        var text = _localizer[key].Value;
        return values.Length == 0
            ? text
            : Placeholder().Replace(text, match =>
            {
                var found = Array.FindIndex(values, v => v.Name == match.Groups[1].Value);
                return found >= 0 ? Convert.ToString(values[found].Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty : match.Value;
            });
    }

    /// <summary>Why a file cannot be read at all.</summary>
    public string Describe(TableProblem problem, TestCaseManagementTransferOptions limits)
    {
        return problem switch
        {
            TableProblem.FileTooLarge => Get("Import:Format:FileTooLarge", ("MaxMegabytes", Megabytes(limits.MaxFileSizeBytes))),
            TableProblem.UnzippedTooLarge => Get("Import:Format:UnzippedTooLarge", ("MaxMegabytes", Megabytes(limits.MaxUncompressedBytes))),
            TableProblem.TooManyRows => Get("Import:Format:TooManyRows", ("MaxRows", limits.MaxRows)),
            TableProblem.Empty => Get("Import:Format:Empty"),
            TableProblem.Unreadable => Get("Import:Format:Unreadable"),
            _ => Get("Import:Format:UnknownFormat"),
        };
    }

    private static string Megabytes(long bytes) => (bytes / (1024d * 1024d)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
