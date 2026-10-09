using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Rtm.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Rtm;

[Authorize(TestCaseManagementPermissions.Requirements.Default)]
public class RtmAppService : TestCaseManagementAppService, IRtmAppService
{
    private readonly IRepository<Requirement, Guid> _requirementRepository;
    private readonly IRepository<RequirementTestCase> _linkRepository;
    private readonly ITestCaseRepository _testCaseRepository;
    private readonly IRtmRepository _rtmRepository;
    private readonly IDefectLinkRepository _defectLinkRepository;

    public RtmAppService(
        IRepository<Requirement, Guid> requirementRepository,
        IRepository<RequirementTestCase> linkRepository,
        ITestCaseRepository testCaseRepository,
        IRtmRepository rtmRepository,
        IDefectLinkRepository defectLinkRepository)
    {
        _requirementRepository = requirementRepository;
        _linkRepository = linkRepository;
        _testCaseRepository = testCaseRepository;
        _rtmRepository = rtmRepository;
        _defectLinkRepository = defectLinkRepository;
    }

    public virtual async Task<RtmMatrixDto> GetMatrixAsync(GetRtmInput input)
    {
        var requirements = await LoadRequirementsAsync(input);
        if (requirements.Count == 0)
        {
            return new RtmMatrixDto();
        }

        var requirementIds = requirements.Select(r => r.Id).ToList();
        var links = await _linkRepository.GetListAsync(x => requirementIds.Contains(x.RequirementId));
        var testCaseIds = links.Select(l => l.TestCaseId).Distinct().ToList();

        // Links to test cases that were soft-deleted are ignored: those test cases are no longer returned here.
        var testCases = (await _testCaseRepository.GetListAsync(x => testCaseIds.Contains(x.Id)))
            .ToDictionary(x => x.Id);

        var results = (await _rtmRepository.GetLatestResultsAsync(
                testCases.Keys.ToList(), input.TestPlanId, input.Environment))
            .ToLookup(r => r.TestCaseId);

        var testCaseStatuses = testCases.Keys.ToDictionary(
            id => id, id => RequirementCoverageCalculator.CalculateTestCaseStatus(results[id]));

        // Deprecated test cases are ignored by the coverage rules, so their defects do not block either.
        var countedTestCaseIds = testCases.Values
            .Where(tc => tc.Status != TestCaseStatus.Deprecated)
            .Select(tc => tc.Id)
            .ToList();
        var blockingDefects = (await _defectLinkRepository.GetOpenListByTestCasesAsync(
                countedTestCaseIds, input.TestPlanId, input.Environment))
            .ToLookup(d => d.TestCaseId);

        var rows = requirements
            .Select(requirement => BuildRow(
                requirement,
                links.Where(l => l.RequirementId == requirement.Id).Select(l => l.TestCaseId).ToList(),
                testCases,
                testCaseStatuses,
                results,
                blockingDefects))
            .ToList();

        var filtered = input.Status.HasValue ? rows.Where(r => r.Status == input.Status).ToList() : rows;

        return new RtmMatrixDto
        {
            Summary = BuildSummary(rows),
            TotalCount = filtered.Count,
            Requirements = filtered.Skip(input.SkipCount).Take(input.MaxResultCount).ToList(),
        };
    }

    protected virtual async Task<List<Requirement>> LoadRequirementsAsync(GetRtmInput input)
    {
        var query = (await _requirementRepository.GetQueryableAsync())
            .WhereIf(input.ProjectId.HasValue, x => x.ProjectId == input.ProjectId)
            .WhereIf(input.MilestoneId.HasValue, x => x.MilestoneId == input.MilestoneId);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var text = input.Filter.Trim().ToLowerInvariant();
            query = query.Where(x => x.Code.ToLower().Contains(text) || x.Title.ToLower().Contains(text));
        }

        return await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Code));
    }

    private static RequirementCoverageDto BuildRow(
        Requirement requirement,
        IReadOnlyCollection<Guid> linkedTestCaseIds,
        IReadOnlyDictionary<Guid, TestCases.TestCase> testCases,
        IReadOnlyDictionary<Guid, TestResultStatus> testCaseStatuses,
        ILookup<Guid, LatestTestResult> results,
        ILookup<Guid, DefectLinkWithContext> blockingDefects)
    {
        var linked = linkedTestCaseIds
            .Where(testCases.ContainsKey)
            .Select(id => testCases[id])
            .OrderBy(tc => tc.Code)
            .ToList();
        var counted = linked.Where(tc => tc.Status != TestCaseStatus.Deprecated).ToList();

        return new RequirementCoverageDto
        {
            RequirementId = requirement.Id,
            Code = requirement.Code,
            Title = requirement.Title,
            Priority = requirement.Priority,
            MilestoneId = requirement.MilestoneId,
            Status = RequirementCoverageCalculator.CalculateRequirementStatus(
                counted.Select(tc => testCaseStatuses[tc.Id]).ToList()),
            TestCases = linked.Select(tc =>
            {
                var current = RequirementCoverageCalculator.CurrentResults(results[tc.Id]);
                return new RequirementTestCaseDto
                {
                    TestCaseId = tc.Id,
                    Code = tc.Code,
                    Title = tc.Title,
                    Status = tc.Status,
                    CountsTowardCoverage = tc.Status != TestCaseStatus.Deprecated,
                    Result = testCaseStatuses[tc.Id],
                    LastExecutedTime = current.Count == 0 ? null : current.Max(r => r.ExecutedTime),
                };
            }).ToList(),
            BlockingDefects = counted
                .SelectMany(tc => blockingDefects[tc.Id].Select(d => new RequirementDefectDto
                {
                    TestCaseId = tc.Id,
                    TestCaseCode = tc.Code,
                    TestExecutionId = d.Link.TestExecutionId,
                    ExternalSystem = d.Link.ExternalSystem,
                    IssueKey = d.Link.IssueKey,
                    IssueUrl = d.Link.IssueUrl,
                    Severity = d.Link.Severity,
                }))
                .ToList(),
        };
    }

    private static RtmSummaryDto BuildSummary(IReadOnlyCollection<RequirementCoverageDto> rows)
    {
        var total = rows.Count;
        int Count(RequirementCoverageStatus status) => rows.Count(r => r.Status == status);
        double Percent(int part) => total == 0 ? 0 : Math.Round(part * 100.0 / total, 2);

        var uncovered = Count(RequirementCoverageStatus.Uncovered);
        var passed = Count(RequirementCoverageStatus.Passed);

        return new RtmSummaryDto
        {
            TotalRequirements = total,
            CoveredRequirements = total - uncovered,
            UncoveredRequirements = uncovered,
            CoveragePercentage = Percent(total - uncovered),
            PassedRequirements = passed,
            PassedPercentage = Percent(passed),
            FailedRequirements = Count(RequirementCoverageStatus.Failed),
            BlockedRequirements = Count(RequirementCoverageStatus.Blocked),
            NotRunRequirements = Count(RequirementCoverageStatus.NotRun),
        };
    }
}
