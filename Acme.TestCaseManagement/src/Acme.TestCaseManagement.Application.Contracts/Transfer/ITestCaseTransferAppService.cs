using Acme.TestCaseManagement.Transfer.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Transfer;

/// <summary>Excel and CSV import and export of the test case library.</summary>
public interface ITestCaseTransferAppService : IApplicationService
{
    /// <summary>
    /// Exports the test cases that match the filters, one row per step, with the columns Suite, Code, Title,
    /// Description, Preconditions, Postconditions, Priority, Severity, Kind, Layer, ExecutionType, AutomationId, Flaky,
    /// Status, Version, StepNo, Action, ExpectedResult and TestData. The file can be imported again.
    /// </summary>
    Task<IRemoteStreamContent> ExportAsync(ExportTestCasesInput input);

    /// <summary>
    /// Imports test cases from an Excel or CSV file. A test case is the rows that share a Code, one step per row. New
    /// test cases are Draft; the status is never imported. The file is checked completely first: while any row is
    /// invalid nothing is written. Use DryRun to see the report without importing.
    /// </summary>
    Task<ImportReportDto> ImportAsync(ImportTestCasesInput input);
}
