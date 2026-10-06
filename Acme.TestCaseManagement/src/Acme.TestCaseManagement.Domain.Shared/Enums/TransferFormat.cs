namespace Acme.TestCaseManagement.Enums;

/// <summary>File format of an export. An import detects the format from the content.</summary>
public enum TransferFormat
{
    /// <summary>Comma separated text, UTF-8 with a byte order mark so that Excel shows diacritics correctly.</summary>
    Csv = 0,

    /// <summary>Excel workbook (.xlsx).</summary>
    Xlsx = 1,
}
