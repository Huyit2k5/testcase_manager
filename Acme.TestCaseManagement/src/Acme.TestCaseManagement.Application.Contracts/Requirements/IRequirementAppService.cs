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
}
