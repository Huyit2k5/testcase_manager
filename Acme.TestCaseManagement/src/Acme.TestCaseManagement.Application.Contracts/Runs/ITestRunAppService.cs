using Acme.TestCaseManagement.Runs.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Runs;

public interface ITestRunAppService : IApplicationService
{
    Task<TestRunDto> GetAsync(Guid id);

    /// <summary>The test cases assigned to the caller in the runs that are not completed, across runs and projects.</summary>
    Task<MyWorkDto> GetMyWorkAsync(GetMyWorkInput input);

    /// <summary>Supported sort columns: title, environment, status, creationTime (append " desc" to reverse).</summary>
    Task<PagedResultDto<TestRunDto>> GetListAsync(GetTestRunListInput input);

    /// <summary>Creates a run and optionally schedules approved test cases in it.</summary>
    Task<TestRunDto> CreateAsync(CreateTestRunDto input);

    /// <summary>Schedules more approved test cases, each bound to its current version.</summary>
    Task<TestRunDto> AddItemsAsync(Guid id, AddTestRunItemsDto input);

    Task<TestRunDto> AssignTesterAsync(Guid id, Guid itemId, AssignTestRunItemDto input);

    /// <summary>Assigns the same person (or nobody) to many items of the run at once. Every item must be in the run, or nothing changes.</summary>
    Task<TestRunDto> AssignTestersAsync(Guid id, AssignTestRunItemsDto input);

    /// <summary>Appends a new attempt for the item. Earlier attempts are never changed.</summary>
    Task<TestExecutionDto> ExecuteItemAsync(Guid id, Guid itemId, ExecuteTestItemDto input);

    /// <summary>Records many results in one transaction.</summary>
    Task<List<TestExecutionDto>> BatchExecuteAsync(Guid id, BatchExecuteTestItemsDto input);

    /// <summary>The attempt history of an item, oldest first.</summary>
    Task<List<TestExecutionDto>> GetExecutionsAsync(Guid id, Guid itemId);

    /// <summary>Links a Failed attempt to an external issue (Jira, GitHub, ...) after the fact.</summary>
    Task<DefectLinkDto> AddDefectLinkAsync(Guid executionId, AddDefectLinkDto input);

    /// <summary>Changes the severity and/or marks the defect resolved (or reopens it).</summary>
    Task<DefectLinkDto> UpdateDefectLinkAsync(Guid executionId, Guid defectLinkId, UpdateDefectLinkDto input);

    /// <summary>The defects linked to one attempt, oldest first.</summary>
    Task<List<DefectLinkDto>> GetDefectLinksAsync(Guid executionId);

    /// <summary>Soft-deletes a link that was entered by mistake. The execution is not changed.</summary>
    Task RemoveDefectLinkAsync(Guid executionId, Guid defectLinkId);

    /// <summary>Closes the run. A completed run accepts no more items or attempts.</summary>
    Task<TestRunDto> CompleteAsync(Guid id);

    /// <summary>
    /// Deletes a run that has no result yet (nothing was executed in it), for example one made by mistake. A run with even one
    /// attempt is part of the history and is completed, not deleted.
    /// </summary>
    Task DeleteAsync(Guid id);
}
