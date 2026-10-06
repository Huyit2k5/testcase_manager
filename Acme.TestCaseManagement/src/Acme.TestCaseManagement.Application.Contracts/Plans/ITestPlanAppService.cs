using Acme.TestCaseManagement.Plans.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Plans;

public interface ITestPlanAppService : IApplicationService
{
    Task<TestPlanDto> GetAsync(Guid id);

    /// <summary>Supported sort columns: name, startDate, endDate, status, creationTime (append " desc" to reverse).</summary>
    Task<PagedResultDto<TestPlanDto>> GetListAsync(GetTestPlanListInput input);

    Task<TestPlanDto> CreateAsync(CreateTestPlanDto input);

    Task<TestPlanDto> UpdateAsync(Guid id, UpdateTestPlanDto input);

    /// <summary>Soft-deletes a plan that has no test runs.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Draft to Active or Archived, Active to Completed or Archived, Completed to Archived.</summary>
    Task<TestPlanDto> ChangeStatusAsync(Guid id, ChangeTestPlanStatusDto input);
}
