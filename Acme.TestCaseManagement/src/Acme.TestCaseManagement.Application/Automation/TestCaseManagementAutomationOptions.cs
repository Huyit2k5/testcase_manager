namespace Acme.TestCaseManagement.Automation;

/// <summary>
/// Limits of publishing automated results. Change them in the host with
/// <c>Configure&lt;TestCaseManagementAutomationOptions&gt;</c>.
/// </summary>
public class TestCaseManagementAutomationOptions
{
    /// <summary>Most results one publish request may carry. Default 2,000; a larger suite is sent in several requests.</summary>
    public int MaxResultsPerRequest { get; set; } = 2_000;
}
