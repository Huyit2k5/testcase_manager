using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Transfer.Tabular;

/// <summary>Reads and writes a <see cref="Table"/> as an Excel or CSV file, detecting the format by content.</summary>
internal static class TableFile
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string CsvContentType = "text/csv; charset=utf-8";

    /// <summary>Reads the file. The format comes from the bytes, not from the name or the content type, which a client can get wrong.</summary>
    public static Table Read(byte[] content, TestCaseManagementTransferOptions limits)
    {
        if (content.Length > limits.MaxFileSizeBytes)
        {
            throw new TableException(TableProblem.FileTooLarge);
        }

        if (XlsxTable.LooksLikeXlsx(content))
        {
            return XlsxTable.Read(content, limits.MaxRows, limits.MaxUncompressedBytes);
        }

        if (LooksBinary(content))
        {
            throw new TableException(TableProblem.UnknownFormat);
        }

        return CsvTable.Read(content, limits.MaxRows);
    }

    public static byte[] Write(Table table, TransferFormat format, string sheetName)
    {
        return format == TransferFormat.Xlsx ? XlsxTable.Write(table, sheetName) : CsvTable.Write(table);
    }

    public static string ContentType(TransferFormat format) => format == TransferFormat.Xlsx ? XlsxContentType : CsvContentType;

    public static string Extension(TransferFormat format) => format == TransferFormat.Xlsx ? ".xlsx" : ".csv";

    /// <summary>
    /// Text has no NUL bytes, apart from UTF-16 and UTF-32 that announce themselves with a byte order mark. An old .xls
    /// file, a PDF or an image does, and must not be parsed as CSV.
    /// </summary>
    private static bool LooksBinary(byte[] content)
    {
        var hasUnicodeMark = content.Length >= 2 && (content[0] == 0xFF && content[1] == 0xFE || content[0] == 0xFE && content[1] == 0xFF);
        return !hasUnicodeMark && Array.IndexOf(content, (byte)0, 0, Math.Min(content.Length, 4096)) >= 0;
    }
}
