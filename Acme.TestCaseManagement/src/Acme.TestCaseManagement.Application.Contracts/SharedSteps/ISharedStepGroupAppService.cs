using Acme.TestCaseManagement.SharedSteps.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.SharedSteps;

/// <summary>
/// The library of reusable groups of steps (FR-005). A test case copies a group (see <c>ITestCaseAppService.InsertSharedStepsAsync</c>),
/// so changing a group changes no test case by itself; it makes the test cases that copied an older revision show as behind.
/// </summary>
public interface ISharedStepGroupAppService : IApplicationService
{
    /// <summary>Every group, by name, with its number of steps and of the test cases that use it.</summary>
    Task<List<SharedStepGroupSummaryDto>> GetListAsync(GetSharedStepGroupsInput input);

    Task<SharedStepGroupDto> GetAsync(Guid id);

    Task<SharedStepGroupDto> CreateAsync(CreateUpdateSharedStepGroupDto input);

    /// <summary>
    /// Changes the group. When its steps change the revision goes up; a new name or description does not raise it. No test case
    /// is touched.
    /// </summary>
    Task<SharedStepGroupDto> UpdateAsync(Guid id, CreateUpdateSharedStepGroupDto input);

    /// <summary>Deletes a group that no test case uses; one that is used is refused.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>The test cases that use the group, and which of them are behind its current revision.</summary>
    Task<List<SharedStepUsageDto>> GetUsageAsync(Guid id);

    /// <summary>
    /// Brings test cases up to date with the group: their copy is replaced by the current steps. An approved test case gets a new
    /// version, so its history keeps what was tested before. Needs the permission to update test cases as well.
    /// </summary>
    Task<UpdateSharedStepUsersResultDto> UpdateTestCasesAsync(Guid id, UpdateSharedStepUsersInput input);
}
