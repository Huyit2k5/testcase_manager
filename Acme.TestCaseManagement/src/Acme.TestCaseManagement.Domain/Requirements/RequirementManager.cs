using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.TestCases;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.Requirements;

public class RequirementManager : DomainService
{
    private readonly IRepository<Requirement, Guid> _requirementRepository;
    private readonly IRepository<RequirementTestCase> _linkRepository;
    private readonly ITestCaseRepository _testCaseRepository;
    private readonly IDataFilter _dataFilter;
    private readonly ProjectManager _projectManager;
    private readonly TestCaseHistoryRecorder _history;

    public RequirementManager(
        IRepository<Requirement, Guid> requirementRepository,
        IRepository<RequirementTestCase> linkRepository,
        ITestCaseRepository testCaseRepository,
        IDataFilter dataFilter,
        ProjectManager projectManager,
        TestCaseHistoryRecorder history)
    {
        _history = history;
        _projectManager = projectManager;
        _requirementRepository = requirementRepository;
        _linkRepository = linkRepository;
        _testCaseRepository = testCaseRepository;
        _dataFilter = dataFilter;
    }

    /// <summary>Builds a requirement after checking its code is free (ignoring case). The caller inserts it.</summary>
    public virtual async Task<Requirement> CreateAsync(string code, string title, Guid? projectId = null)
    {
        var project = await _projectManager.ResolveForNewAsync(projectId);
        var requirement = new Requirement(GuidGenerator.Create(), CurrentTenant.Id, code, title);
        requirement.SetProject(project.Id);
        await EnsureCodeIsUniqueAsync(requirement.Code, exceptRequirementId: null);

        return requirement;
    }

    public virtual async Task ChangeCodeAsync(Requirement requirement, string newCode)
    {
        var code = newCode?.Trim() ?? string.Empty;
        if (string.Equals(requirement.Code, code, StringComparison.Ordinal))
        {
            return;
        }

        await EnsureCodeIsUniqueAsync(code, requirement.Id);
        requirement.SetCode(code);
    }

    /// <summary>Links a test case. Returns false when the link already exists. A removed link is restored.</summary>
    public virtual async Task<bool> LinkTestCaseAsync(Requirement requirement, Guid testCaseId)
    {
        var testCase = await _testCaseRepository.GetAsync(testCaseId, includeDetails: false); // 404 when it does not exist
        await _projectManager.EnsureSameProjectAsync("A requirement and a test case", requirement.ProjectId, await _projectManager.GetProjectOfSuiteAsync(testCase.SuiteId));

        RequirementTestCase? link;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            link = await _linkRepository.FindAsync(x => x.RequirementId == requirement.Id && x.TestCaseId == testCaseId);
        }

        if (link == null)
        {
            await _linkRepository.InsertAsync(
                new RequirementTestCase(requirement.TenantId, requirement.Id, testCaseId), autoSave: true);
            return true;
        }

        if (!link.IsDeleted)
        {
            return false;
        }

        link.Restore();
        await _linkRepository.UpdateAsync(link, autoSave: true);
        return true;
    }

    /// <summary>Soft-deletes the link. Returns false when there was no active link.</summary>
    public virtual async Task<bool> UnlinkTestCaseAsync(Requirement requirement, Guid testCaseId)
    {
        var link = await _linkRepository.FindAsync(x => x.RequirementId == requirement.Id && x.TestCaseId == testCaseId);
        if (link == null)
        {
            return false;
        }

        await _linkRepository.DeleteAsync(link, autoSave: true);
        return true;
    }

    /// <summary>The fields of a requirement whose change makes its test cases suspect: what the tests verify against.</summary>
    public static readonly IReadOnlyList<string> WatchedFields = new[] { "Title", "Description", "AcceptanceCriteria" };

    /// <summary>
    /// A requirement changed in <paramref name="changedFields"/>: every test case linked to it is to be checked against the new text.
    /// Each link is marked (and the test case gets a line in its history); returns how many test cases that is.
    /// </summary>
    public virtual async Task<int> MarkLinksSuspectAsync(Requirement requirement, IReadOnlyCollection<string> changedFields)
    {
        if (changedFields.Count == 0)
        {
            return 0;
        }

        var links = await _linkRepository.GetListAsync(x => x.RequirementId == requirement.Id);
        foreach (var link in links)
        {
            link.MarkSuspect(Clock.Now, changedFields);
        }

        await _linkRepository.UpdateManyAsync(links, autoSave: true);
        foreach (var link in links)
        {
            await _history.RecordAsync(link.TestCaseId, TestCaseHistoryType.RequirementChanged, note: $"{requirement.Code}: {string.Join(", ", changedFields)}");
        }

        return links.Count;
    }

    /// <summary>Confirms that the test case still matches the requirement. Returns false when the link is not suspect (or does not exist).</summary>
    public virtual async Task<bool> ReviewLinkAsync(Requirement requirement, Guid testCaseId)
    {
        var link = await _linkRepository.FindAsync(x => x.RequirementId == requirement.Id && x.TestCaseId == testCaseId);
        if (link == null || !link.IsSuspect)
        {
            return false;
        }

        link.ClearSuspect();
        await _linkRepository.UpdateAsync(link, autoSave: true);
        await _history.RecordAsync(testCaseId, TestCaseHistoryType.RequirementReviewed, note: requirement.Code);
        return true;
    }

    /// <summary>Confirms every suspect link of the requirement at once; returns how many.</summary>
    public virtual async Task<int> ReviewAllLinksAsync(Requirement requirement)
    {
        var links = await _linkRepository.GetListAsync(x => x.RequirementId == requirement.Id && x.IsSuspect);
        foreach (var link in links)
        {
            link.ClearSuspect();
        }

        await _linkRepository.UpdateManyAsync(links, autoSave: true);
        foreach (var link in links)
        {
            await _history.RecordAsync(link.TestCaseId, TestCaseHistoryType.RequirementReviewed, note: requirement.Code);
        }

        return links.Count;
    }

    protected virtual async Task EnsureCodeIsUniqueAsync(string code, Guid? exceptRequirementId)
    {
        var lowered = code.ToLowerInvariant();
        var existing = await _requirementRepository.FindAsync(x => x.Code.ToLower() == lowered);

        if (existing != null && existing.Id != exceptRequirementId)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DuplicateRequirementCode).WithData("Code", code);
        }
    }
}
