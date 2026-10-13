using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.SignOff.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.SignOff;

public interface ISignOffAppService : IApplicationService
{
    Task<SignOffReportDto> GetAsync(Guid id);

    /// <summary>Supported sort columns: creationTime, title, status (append " desc" to reverse; default newest first).</summary>
    Task<PagedResultDto<SignOffReportDto>> GetListAsync(GetSignOffListInput input);

    /// <summary>
    /// Starts a sign-off for a test plan or milestone as the current user. The quality gate is evaluated first: when it
    /// does not pass, the call is rejected with the failed criteria. When it passes, a report is created that freezes
    /// the evaluation (JSON plus SHA-256) and holds the caller's approval; it becomes Approved once the gate's required
    /// number of distinct users have approved. Any pending report of the same scope is superseded.
    /// </summary>
    Task<SignOffReportDto> SignOffAsync(StartSignOffDto input);

    /// <summary>
    /// Adds the current user's approval to a pending report. The gate is evaluated again with the thresholds frozen in
    /// the report and the approval is refused if it no longer passes. A user can approve once.
    /// </summary>
    Task<SignOffReportDto> ApproveAsync(Guid id, ApproveSignOffDto input);

    /// <summary>
    /// The sign-off report as a file to keep, print and sign: a PDF (A4, with places for the signatures) or an Excel workbook (one sheet for the
    /// report, and one for the criteria, the open defects and the approvals). It states what was frozen when the sign-off was made (the
    /// figures, the gate, who approved, the hash and whether it still matches), in the language of the caller. Needs the permission to read sign-offs.
    /// </summary>
    Task<IRemoteStreamContent> ExportAsync(Guid id, ReportFormat format = ReportFormat.Pdf);
}
