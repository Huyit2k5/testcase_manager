using Acme.TestCaseManagement.Requirements.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Requirements;

public interface IRequirementAppService : IApplicationService
{
    Task<RequirementDto> GetAsync(Guid id);

    /// <summary>Supported sort columns: code, title, priority, creationTime (append " desc" to reverse).</summary>
    Task<PagedResultDto<RequirementDto>> GetListAsync(GetRequirementListInput input);

    Task<RequirementDto> CreateAsync(CreateUpdateRequirementDto input);

    Task<RequirementDto> UpdateAsync(Guid id, CreateUpdateRequirementDto input);

    /// <summary>Soft-deletes the requirement. It disappears from the RTM; its history stays in the audit trail.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Links test cases to the requirement. Already linked ones are ignored.</summary>
    Task LinkTestCasesAsync(Guid id, LinkTestCasesDto input);

    /// <summary>Removes the link (soft delete). Does nothing when it does not exist.</summary>
    Task UnlinkTestCaseAsync(Guid id, Guid testCaseId);

    /// <summary>The requirements a test case is linked to, with whether it is to be checked against each, ordered by code.</summary>
    Task<List<RequirementLinkDto>> GetForTestCaseAsync(Guid testCaseId);

    /// <summary>
    /// Confirms that the test case still matches the requirement after it changed; the link stops being suspect. Needs the permission to
    /// update test cases, which is what looking after a test case is. Does nothing when the link is not suspect.
    /// </summary>
    Task<RequirementReviewResultDto> ReviewLinkAsync(Guid id, Guid testCaseId);

    /// <summary>Confirms every suspect test case of the requirement at once.</summary>
    Task<RequirementReviewResultDto> ReviewAllAsync(Guid id);
}
