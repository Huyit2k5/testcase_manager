using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.TestCases;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.Quality;

public class DefectLinkManager : DomainService
{
    private readonly IRepository<DefectLink, Guid> _defectRepository;
    private readonly IRepository<TestRunItem, Guid> _itemRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;
    private readonly ITestCaseRepository _testCaseRepository;

    public DefectLinkManager(
        IRepository<DefectLink, Guid> defectRepository,
        IRepository<TestRunItem, Guid> itemRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        ITestCaseRepository testCaseRepository)
    {
        _defectRepository = defectRepository;
        _itemRepository = itemRepository;
        _versionRepository = versionRepository;
        _testCaseRepository = testCaseRepository;
    }

    /// <summary>
    /// Links <paramref name="execution"/> to an external issue and saves the link. Only Failed executions can carry
    /// a defect, and the same issue (system and key, ignoring case) can be linked to an execution once.
    /// When <paramref name="severity"/> is not given the severity of the failing test case is used, so a forgotten
    /// severity can never hide a critical defect from the quality gate.
    /// </summary>
    public virtual async Task<DefectLink> AddAsync(
        TestExecution execution, string externalSystem, string issueKey, string? issueUrl, SeverityLevel? severity = null)
    {
        if (execution.Status != TestResultStatus.Failed)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DefectRequiresFailedExecution)
                .WithData("AttemptNumber", execution.AttemptNumber)
                .WithData("Status", execution.Status);
        }

        var link = new DefectLink(
            GuidGenerator.Create(),
            execution.TenantId,
            execution.Id,
            externalSystem,
            issueKey,
            issueUrl,
            severity ?? await GetTestCaseSeverityAsync(execution));

        var existing = await _defectRepository.GetListAsync(x => x.TestExecutionId == execution.Id);
        if (existing.Any(x =>
                string.Equals(x.ExternalSystem, link.ExternalSystem, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.IssueKey, link.IssueKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DuplicateDefectLink)
                .WithData("IssueKey", link.IssueKey)
                .WithData("ExternalSystem", link.ExternalSystem);
        }

        // Saved immediately so that a second link in the same request is checked against this one.
        return await _defectRepository.InsertAsync(link, autoSave: true);
    }

    /// <summary>Changes the severity and/or the resolved state of a link and saves it.</summary>
    public virtual async Task<DefectLink> UpdateAsync(DefectLink link, SeverityLevel severity, bool isResolved)
    {
        link.Update(severity, isResolved, Clock.Now);

        return await _defectRepository.UpdateAsync(link, autoSave: true);
    }

    /// <summary>Severity of the test case the execution belongs to; Medium when it cannot be found any more.</summary>
    protected virtual async Task<SeverityLevel> GetTestCaseSeverityAsync(TestExecution execution)
    {
        var item = await _itemRepository.FindAsync(execution.TestRunItemId);
        var version = item == null ? null : await _versionRepository.FindAsync(item.TestCaseVersionId);
        var testCase = version == null ? null : await _testCaseRepository.FindAsync(version.TestCaseId, includeDetails: false);

        return testCase?.Severity ?? SeverityLevel.Medium;
    }
}
