namespace Acme.TestCaseManagement.Controllers;

/// <summary>The content types of an export, as constants so that the controllers can document them.</summary>
internal static class TableContentTypes
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string Csv = "text/csv";
}
