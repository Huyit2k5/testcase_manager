using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.Suites;

public class TestSuiteManager : DomainService
{
    private readonly IRepository<TestSuite, Guid> _suiteRepository;

    public TestSuiteManager(IRepository<TestSuite, Guid> suiteRepository)
    {
        _suiteRepository = suiteRepository;
    }

    /// <summary>Builds a new suite appended after its siblings. The caller inserts it.</summary>
    public virtual async Task<TestSuite> CreateAsync(string name, Guid? parentId, string? description = null)
    {
        if (parentId.HasValue)
        {
            await GetOrThrowAsync(parentId.Value);
        }

        var siblings = await GetSiblingsAsync(parentId);
        var order = siblings.Count == 0 ? 0 : siblings.Max(x => x.Order) + 1;

        return new TestSuite(GuidGenerator.Create(), CurrentTenant.Id, name, parentId, order, description);
    }

    /// <summary>
    /// Throws <see cref="CircularSuiteDependencyException"/> when <paramref name="newParentId"/> is the suite
    /// itself or one of its descendants. Walks the ancestor chain of the new parent, so the cost is the tree depth.
    /// </summary>
    public virtual async Task ValidateParentHierarchyAsync(Guid suiteId, Guid newParentId)
    {
        var visited = new HashSet<Guid>();
        Guid? currentId = newParentId;

        while (currentId.HasValue)
        {
            if (currentId.Value == suiteId)
            {
                var suite = await _suiteRepository.FindAsync(suiteId);
                throw new CircularSuiteDependencyException(suite?.Name ?? suiteId.ToString());
            }

            if (!visited.Add(currentId.Value))
            {
                // Pre-existing cycle that does not involve suiteId; stop instead of looping forever.
                break;
            }

            var current = await GetOrThrowAsync(currentId.Value);
            currentId = current.ParentId;
        }
    }

    /// <summary>Re-parents <paramref name="suite"/> and places it at <paramref name="newOrder"/> among its new siblings.</summary>
    public virtual async Task MoveAsync(TestSuite suite, Guid? newParentId, int newOrder)
    {
        if (newParentId.HasValue)
        {
            await ValidateParentHierarchyAsync(suite.Id, newParentId.Value);
        }

        var oldParentId = suite.ParentId;
        var changed = new List<TestSuite>();

        var newSiblings = (await GetSiblingsAsync(newParentId)).Where(x => x.Id != suite.Id).ToList();
        var position = Math.Clamp(newOrder, 0, newSiblings.Count);
        newSiblings.Insert(position, suite);
        Renumber(newSiblings, changed);

        suite.MoveTo(newParentId, position);
        if (!changed.Contains(suite))
        {
            changed.Add(suite);
        }

        if (oldParentId != newParentId)
        {
            var oldSiblings = (await GetSiblingsAsync(oldParentId)).Where(x => x.Id != suite.Id).ToList();
            Renumber(oldSiblings, changed);
        }

        await _suiteRepository.UpdateManyAsync(changed, autoSave: true);
    }

    protected virtual async Task<TestSuite> GetOrThrowAsync(Guid id)
    {
        var suite = await _suiteRepository.FindAsync(id);
        if (suite == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SuiteNotFound).WithData("SuiteId", id);
        }

        return suite;
    }

    protected virtual async Task<List<TestSuite>> GetSiblingsAsync(Guid? parentId)
    {
        var siblings = await _suiteRepository.GetListAsync(x => x.ParentId == parentId);
        return siblings.OrderBy(x => x.Order).ThenBy(x => x.Name).ToList();
    }

    private static void Renumber(List<TestSuite> ordered, List<TestSuite> changed)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Order != i)
            {
                ordered[i].SetOrder(i);
                if (!changed.Contains(ordered[i]))
                {
                    changed.Add(ordered[i]);
                }
            }
        }
    }
}
