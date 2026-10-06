using Acme.TestCaseManagement.SignOff.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

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
}
