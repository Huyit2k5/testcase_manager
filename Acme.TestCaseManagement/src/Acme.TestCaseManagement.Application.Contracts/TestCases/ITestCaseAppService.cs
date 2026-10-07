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
    /// Replaces the tags of a test case. Tags are labels, so this publishes no version and needs no approval, even for an approved
    /// test case.
    /// </summary>
    Task<TestCaseDto> SetTagsAsync(Guid id, SetTestCaseTagsDto input);

    /// <summary>
    /// Copies the steps of a group of shared steps into the test case, linked to the group's current revision. An approved test case
    /// gets a new version, as for any change of its steps.
    /// </summary>
    Task<TestCaseDto> InsertSharedStepsAsync(Guid id, InsertSharedStepsDto input);

    /// <summary>Replaces the steps that came from the group by its current steps. An approved test case gets a new version.</summary>
    Task<TestCaseDto> RefreshSharedStepsAsync(Guid id, Guid groupId, RefreshSharedStepsDto input);

    /// <summary>
    /// Makes the steps that came from the group the test case's own. Their content does not change, so no version is published.
    /// </summary>
    Task<TestCaseDto> DetachSharedStepsAsync(Guid id, Guid groupId);

    /// <summary>Every tag in use with the number of test cases that have it, the most used first; for a filter or a suggestion.</summary>
    Task<List<TagSummaryDto>> GetTagsAsync();

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
