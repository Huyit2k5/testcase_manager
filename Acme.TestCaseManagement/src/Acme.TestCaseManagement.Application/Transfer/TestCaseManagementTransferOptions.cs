namespace Acme.TestCaseManagement.Transfer;

/// <summary>
/// Limits of the Excel and CSV import and export. Change them in the host with
/// <c>Configure&lt;TestCaseManagementTransferOptions&gt;</c>.
/// </summary>
public class TestCaseManagementTransferOptions
{
    /// <summary>Largest file an import accepts, in bytes. Default 5 MiB.</summary>
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Most data rows (not counting the header) that an import reads. Default 10,000.</summary>
    public int MaxRows { get; set; } = 10_000;

    /// <summary>
    /// Largest size an Excel file may have once unzipped, in bytes. Default 50 MiB. It stops a small file that expands
    /// into something huge (a "zip bomb").
    /// </summary>
    public long MaxUncompressedBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Most test cases an export writes. Default 20,000.</summary>
    public int MaxExportTestCases { get; set; } = 20_000;
}
