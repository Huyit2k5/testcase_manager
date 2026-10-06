using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Suites;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.TestCases;

public class TestCaseManager : DomainService
{
    private static readonly IReadOnlyDictionary<TestCaseStatus, TestCaseStatus[]> AllowedTransitions =
        new Dictionary<TestCaseStatus, TestCaseStatus[]>
        {
            [TestCaseStatus.Draft] = [TestCaseStatus.UnderReview, TestCaseStatus.Approved],
            [TestCaseStatus.UnderReview] = [TestCaseStatus.Draft, TestCaseStatus.Approved],
            [TestCaseStatus.Approved] = [TestCaseStatus.Draft, TestCaseStatus.Deprecated],
            [TestCaseStatus.Deprecated] = [TestCaseStatus.Draft],
        };

    private readonly ITestCaseRepository _testCaseRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;
    private readonly IRepository<TestSuite, Guid> _suiteRepository;

    public TestCaseManager(
        ITestCaseRepository testCaseRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        IRepository<TestSuite, Guid> suiteRepository)
    {
        _testCaseRepository = testCaseRepository;
        _versionRepository = versionRepository;
        _suiteRepository = suiteRepository;
    }

    /// <summary>Builds a new Draft test case after checking the suite exists and the code is free. The caller inserts it.</summary>
    public virtual async Task<TestCase> CreateAsync(Guid suiteId, string code, string title)
    {
        await EnsureSuiteExistsAsync(suiteId);
        await EnsureCodeIsUniqueAsync(code, exceptTestCaseId: null);

        return new TestCase(GuidGenerator.Create(), CurrentTenant.Id, suiteId, code, title);
    }

    public virtual async Task ChangeCodeAsync(TestCase testCase, string newCode)
    {
        var code = newCode?.Trim() ?? string.Empty;
        if (string.Equals(testCase.Code, code, StringComparison.Ordinal))
        {
            return;
        }

        await EnsureCodeIsUniqueAsync(code, testCase.Id);
        testCase.SetCode(code);
    }

    public virtual async Task ChangeSuiteAsync(TestCase testCase, Guid suiteId)
    {
        if (testCase.SuiteId == suiteId)
        {
            return;
        }

        await EnsureSuiteExistsAsync(suiteId);
        testCase.SetSuite(suiteId);
    }

    /// <summary>
    /// Moves the test case to <paramref name="target"/>. Reaching <see cref="TestCaseStatus.Approved"/> requires at
    /// least one step and publishes a new <see cref="TestCaseVersion"/>, which is returned; other transitions return null.
    /// </summary>
    public virtual async Task<TestCaseVersion?> ChangeStatusAsync(TestCase testCase, TestCaseStatus target, string? changeSummary)
    {
        if (!AllowedTransitions.TryGetValue(testCase.Status, out var allowed) || !allowed.Contains(target))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidTestCaseStatusTransition)
                .WithData("From", testCase.Status)
                .WithData("To", target);
        }

        if (target == TestCaseStatus.Approved && testCase.Steps.Count == 0)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestCaseHasNoSteps)
                .WithData("Code", testCase.Code);
        }

        testCase.SetStatus(target);

        return target == TestCaseStatus.Approved
            ? await PublishNewVersionAsync(testCase, changeSummary)
            : null;
    }

    public virtual async Task<TestCaseVersion> ApproveAsync(TestCase testCase, string? changeSummary)
    {
        return (await ChangeStatusAsync(testCase, TestCaseStatus.Approved, changeSummary))!;
    }

    /// <summary>Freezes the current content and steps into a new immutable version and advances <see cref="TestCase.CurrentVersion"/>.</summary>
    public virtual async Task<TestCaseVersion> PublishNewVersionAsync(TestCase testCase, string? changeSummary)
    {
        var versionNumber = testCase.IncrementVersion();

        var snapshot = testCase.Steps
            .OrderBy(s => s.StepOrder)
            .Select(s => new TestStepSnapshot(s.Id, s.StepOrder, s.Action, s.ExpectedResult, s.TestData));

        var version = new TestCaseVersion(
            GuidGenerator.Create(),
            testCase.TenantId,
            testCase.Id,
            versionNumber,
            testCase.Title,
            testCase.Preconditions,
            TestStepSnapshot.Serialize(snapshot),
            testCase.Postconditions,
            changeSummary);

        return await _versionRepository.InsertAsync(version);
    }

    protected virtual async Task EnsureCodeIsUniqueAsync(string code, Guid? exceptTestCaseId)
    {
        var normalized = code?.Trim() ?? string.Empty;
        var existing = await _testCaseRepository.FindByCodeAsync(normalized);

        if (existing != null && existing.Id != exceptTestCaseId)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DuplicateTestCaseCode)
                .WithData("Code", normalized);
        }
    }

    protected virtual async Task EnsureSuiteExistsAsync(Guid suiteId)
    {
        if (await _suiteRepository.FindAsync(suiteId) == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SuiteNotFound).WithData("SuiteId", suiteId);
        }
    }
}
