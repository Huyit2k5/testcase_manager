using Acme.TestCaseManagement.Rtm.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Rtm;

public interface IRtmAppService : IApplicationService
{
    /// <summary>
    /// Builds the requirements traceability matrix: each requirement with its linked test cases, results, calculated
    /// status and blocking defects, plus the summary (total, covered %, passed %).
    /// </summary>
    Task<RtmMatrixDto> GetMatrixAsync(GetRtmInput input);
}
