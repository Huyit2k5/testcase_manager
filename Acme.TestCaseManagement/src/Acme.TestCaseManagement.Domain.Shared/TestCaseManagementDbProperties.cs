namespace Acme.TestCaseManagement;

public static class TestCaseManagementDbProperties
{
    /// <summary>Table prefix, producing names such as <c>TcmTestCases</c> and <c>TcmSuites</c>.</summary>
    public static string DbTablePrefix { get; set; } = "Tcm";

    public static string? DbSchema { get; set; } = null;

    public const string ConnectionStringName = "TestCaseManagement";
}
