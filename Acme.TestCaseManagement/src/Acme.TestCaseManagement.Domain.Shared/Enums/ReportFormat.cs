namespace Acme.TestCaseManagement.Enums;

/// <summary>The file a report is written as.</summary>
public enum ReportFormat
{
    /// <summary>A document to read, print and sign.</summary>
    Pdf = 0,

    /// <summary>A workbook to work on: one sheet for the report and one for each table.</summary>
    Xlsx = 1,
}
