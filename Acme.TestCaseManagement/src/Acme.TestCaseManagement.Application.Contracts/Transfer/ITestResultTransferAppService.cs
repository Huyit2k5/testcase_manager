using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Transfer.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Transfer;

/// <summary>Excel and CSV export and import of the execution results of a run.</summary>
public interface ITestResultTransferAppService : IApplicationService
{
    /// <summary>
    /// Exports every attempt of every item of the run, oldest first, with the columns Code, Title, Version, Attempt,
    /// Result, ActualResult, DurationSeconds, Defects, ExecutedAt and ExecutedBy. An item without an attempt has the
    /// result Untested.
    /// </summary>
    Task<IRemoteStreamContent> ExportAsync(Guid runId, TransferFormat format);

    /// <summary>
    /// Records results from an Excel or CSV file as new attempts, matching rows to run items by Code (and Version when
    /// the code is in the run more than once). Earlier attempts are never changed. Rows with the result Untested or no
    /// result are skipped. The file is checked completely first: while any row is invalid nothing is recorded.
    /// </summary>
    Task<ImportReportDto> ImportAsync(Guid runId, ImportTestResultsInput input);
}
