using Acme.TestCaseManagement.TestCases.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.TestCases;

public interface ITestCaseAppService : IApplicationService
{
    Task<TestCaseDto> GetAsync(Guid id);

    Task<PagedResultDto<TestCaseDto>> GetListAsync(GetTestCaseListInput input);

    /// <summary>Creates a Draft test case.</summary>
    Task<TestCaseDto> CreateAsync(CreateUpdateTestCaseDto input);

    /// <summary>Updates a test case. When it is Approved, a new version snapshot is published automatically.</summary>
    Task<TestCaseDto> UpdateAsync(Guid id, CreateUpdateTestCaseDto input);

    Task DeleteAsync(Guid id);

    /// <summary>Reorders the steps. When the test case is Approved, a new version snapshot is published.</summary>
    Task<TestCaseDto> ReorderStepsAsync(Guid id, ReorderTestStepsDto input);

    /// <summary>
    /// Moves the test case through its lifecycle (Draft, UnderReview, Approved, Deprecated).
    /// Approving requires the Approve permission and publishes a version snapshot.
    /// </summary>
    Task<TestCaseDto> ChangeStatusAsync(Guid id, ChangeTestCaseStatusDto input);

    Task<List<TestCaseVersionDto>> GetVersionsAsync(Guid id);

    Task<TestCaseVersionDto> GetVersionAsync(Guid id, int versionNumber);

    /// <summary>
    /// Every defect linked to any execution of this test case, across all runs and versions,
    /// ordered by execution time (oldest first).
    /// </summary>
    Task<List<TestCaseDefectDto>> GetDefectLinksAsync(Guid id);
}
