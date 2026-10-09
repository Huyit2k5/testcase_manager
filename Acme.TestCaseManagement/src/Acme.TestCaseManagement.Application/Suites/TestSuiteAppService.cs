using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Suites.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Suites;

[Authorize(TestCaseManagementPermissions.TestSuites.Default)]
public class TestSuiteAppService : TestCaseManagementAppService, ITestSuiteAppService
{
    private readonly IRepository<TestSuite, Guid> _suiteRepository;
    private readonly ITestCaseRepository _testCaseRepository;
    private readonly TestSuiteManager _suiteManager;

    public TestSuiteAppService(
        IRepository<TestSuite, Guid> suiteRepository,
        ITestCaseRepository testCaseRepository,
        TestSuiteManager suiteManager)
    {
        _suiteRepository = suiteRepository;
        _testCaseRepository = testCaseRepository;
        _suiteManager = suiteManager;
    }

    public virtual async Task<TestSuiteDto> GetAsync(Guid id)
    {
        return ObjectMapper.Map<TestSuite, TestSuiteDto>(await _suiteRepository.GetAsync(id));
    }

    public virtual async Task<List<TestSuiteTreeDto>> GetTreeAsync(Guid? projectId = null)
    {
        var suites = await _suiteRepository.GetListAsync(x => projectId == null || x.ProjectId == projectId);

        var testCaseQuery = await _testCaseRepository.GetQueryableAsync();
        var counts = (await AsyncExecuter.ToListAsync(
                testCaseQuery.GroupBy(x => x.SuiteId).Select(g => new { SuiteId = g.Key, Count = g.Count() })))
            .ToDictionary(x => x.SuiteId, x => x.Count);

        var nodes = suites.ToDictionary(
            s => s.Id,
            s => new TestSuiteTreeDto
            {
                Id = s.Id,
                ParentId = s.ParentId,
                Name = s.Name,
                Description = s.Description,
                Order = s.Order,
                TestCaseCount = counts.GetValueOrDefault(s.Id),
            });

        var roots = new List<TestSuiteTreeDto>();
        foreach (var node in nodes.Values)
        {
            // A suite whose parent is not visible (e.g. other tenant data or inconsistent state) is shown at root level.
            if (node.ParentId.HasValue && nodes.TryGetValue(node.ParentId.Value, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return SortRecursively(roots);
    }

    [Authorize(TestCaseManagementPermissions.TestSuites.Manage)]
    public virtual async Task<TestSuiteDto> CreateAsync(CreateTestSuiteDto input)
    {
        var suite = await _suiteManager.CreateAsync(input.Name, input.ParentId, input.Description, input.ProjectId);
        await _suiteRepository.InsertAsync(suite, autoSave: true);

        return ObjectMapper.Map<TestSuite, TestSuiteDto>(suite);
    }

    [Authorize(TestCaseManagementPermissions.TestSuites.Manage)]
    public virtual async Task<TestSuiteDto> UpdateAsync(Guid id, UpdateTestSuiteDto input)
    {
        var suite = await _suiteRepository.GetAsync(id);
        suite.SetName(input.Name);
        suite.SetDescription(input.Description);
        await _suiteRepository.UpdateAsync(suite, autoSave: true);

        return ObjectMapper.Map<TestSuite, TestSuiteDto>(suite);
    }

    [Authorize(TestCaseManagementPermissions.TestSuites.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var suite = await _suiteRepository.GetAsync(id);

        if (await _suiteRepository.AnyAsync(x => x.ParentId == id) ||
            await _testCaseRepository.AnyAsync(x => x.SuiteId == id))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SuiteNotEmpty).WithData("SuiteName", suite.Name);
        }

        await _suiteRepository.DeleteAsync(suite);
    }

    [Authorize(TestCaseManagementPermissions.TestSuites.Manage)]
    public virtual async Task<TestSuiteDto> MoveAsync(Guid id, MoveTestSuiteDto input)
    {
        var suite = await _suiteRepository.GetAsync(id);
        await _suiteManager.MoveAsync(suite, input.NewParentId, input.NewOrder);

        return ObjectMapper.Map<TestSuite, TestSuiteDto>(suite);
    }

    private static List<TestSuiteTreeDto> SortRecursively(List<TestSuiteTreeDto> nodes)
    {
        var sorted = nodes.OrderBy(x => x.Order).ThenBy(x => x.Name).ToList();
        foreach (var node in sorted)
        {
            node.Children = SortRecursively(node.Children);
        }

        return sorted;
    }
}
