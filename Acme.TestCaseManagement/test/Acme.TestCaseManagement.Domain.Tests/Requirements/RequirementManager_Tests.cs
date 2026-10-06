using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.Requirements;

public class RequirementManager_Tests : TestCaseManagementDomainTestBase
{
    private readonly RequirementManager _manager;
    private readonly TestCaseManager _testCaseManager;
    private readonly TestSuiteManager _suiteManager;
    private readonly IRepository<TestSuite, Guid> _suiteRepository;
    private readonly IRepository<TestCase, Guid> _testCaseRepository;
    private readonly IRepository<Requirement, Guid> _requirementRepository;
    private readonly IRepository<RequirementTestCase> _linkRepository;

    public RequirementManager_Tests()
    {
        _manager = GetRequiredService<RequirementManager>();
        _testCaseManager = GetRequiredService<TestCaseManager>();
        _suiteManager = GetRequiredService<TestSuiteManager>();
        _suiteRepository = GetRequiredService<IRepository<TestSuite, Guid>>();
        _testCaseRepository = GetRequiredService<IRepository<TestCase, Guid>>();
        _requirementRepository = GetRequiredService<IRepository<Requirement, Guid>>();
        _linkRepository = GetRequiredService<IRepository<RequirementTestCase>>();
    }

    private async Task<Requirement> CreateRequirementAsync(string code = "REQ-AUTH-01")
    {
        var requirement = await _manager.CreateAsync(code, "Two-Factor Authentication");
        return await _requirementRepository.InsertAsync(requirement, autoSave: true);
    }

    private async Task<TestCase> CreateTestCaseAsync(string code)
    {
        var suite = (await _suiteRepository.GetListAsync()).FirstOrDefault()
                    ?? await _suiteRepository.InsertAsync(await _suiteManager.CreateAsync("S", null), autoSave: true);
        var testCase = await _testCaseManager.CreateAsync(suite.Id, code, $"Title {code}");
        return await _testCaseRepository.InsertAsync(testCase, autoSave: true);
    }

    [Fact]
    public async Task Create_Should_Reject_A_Duplicate_Code_Ignoring_Case()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await CreateRequirementAsync("REQ-AUTH-01");

            (await Should.ThrowAsync<BusinessException>(() => _manager.CreateAsync("req-auth-01", "Other")))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateRequirementCode);
        });
    }

    [Fact]
    public async Task ChangeCode_Should_Allow_The_Own_Code_And_Reject_Another_Requirements_Code()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var first = await CreateRequirementAsync("REQ-1");
            await CreateRequirementAsync("REQ-2");

            await _manager.ChangeCodeAsync(first, "REQ-1"); // unchanged: fine
            await _manager.ChangeCodeAsync(first, "REQ-3");
            first.Code.ShouldBe("REQ-3");

            (await Should.ThrowAsync<BusinessException>(() => _manager.ChangeCodeAsync(first, "req-2")))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateRequirementCode);
        });
    }

    [Fact]
    public async Task Link_Should_Be_Idempotent_And_Reject_An_Unknown_Test_Case()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var requirement = await CreateRequirementAsync();
            var testCase = await CreateTestCaseAsync("TC-AUTH-005");

            (await _manager.LinkTestCaseAsync(requirement, testCase.Id)).ShouldBeTrue();
            (await _manager.LinkTestCaseAsync(requirement, testCase.Id)).ShouldBeFalse();

            (await _linkRepository.CountAsync(x => x.RequirementId == requirement.Id)).ShouldBe(1);
            await Should.ThrowAsync<EntityNotFoundException>(() => _manager.LinkTestCaseAsync(requirement, Guid.NewGuid()));
        });
    }

    [Fact]
    public async Task Unlink_Should_Soft_Delete_And_A_Later_Link_Should_Restore_The_Same_Row()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var requirement = await CreateRequirementAsync();
            var testCase = await CreateTestCaseAsync("TC-AUTH-005");
            await _manager.LinkTestCaseAsync(requirement, testCase.Id);

            (await _manager.UnlinkTestCaseAsync(requirement, testCase.Id)).ShouldBeTrue();
            (await _manager.UnlinkTestCaseAsync(requirement, testCase.Id)).ShouldBeFalse();
            (await _linkRepository.CountAsync(x => x.RequirementId == requirement.Id)).ShouldBe(0);

            // The row is still there (soft-deleted), so the audit trail is preserved.
            using (GetRequiredService<IDataFilter>().Disable<ISoftDelete>())
            {
                var row = await _linkRepository.GetAsync(x => x.RequirementId == requirement.Id && x.TestCaseId == testCase.Id);
                row.IsDeleted.ShouldBeTrue();
                row.DeletionTime.ShouldNotBeNull();
            }

            (await _manager.LinkTestCaseAsync(requirement, testCase.Id)).ShouldBeTrue();

            var restored = await _linkRepository.GetAsync(x => x.RequirementId == requirement.Id && x.TestCaseId == testCase.Id);
            restored.IsDeleted.ShouldBeFalse();
            restored.DeletionTime.ShouldBeNull();
            restored.DeleterId.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Requirement_Should_Validate_Its_Fields()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await Should.ThrowAsync<ArgumentException>(() => _manager.CreateAsync(" ", "Title"));
            await Should.ThrowAsync<ArgumentException>(() => _manager.CreateAsync("REQ-9", ""));

            var requirement = await _manager.CreateAsync("  REQ-9  ", "  Title  ");
            requirement.Code.ShouldBe("REQ-9");
            requirement.Title.ShouldBe("Title");
        });
    }
}
