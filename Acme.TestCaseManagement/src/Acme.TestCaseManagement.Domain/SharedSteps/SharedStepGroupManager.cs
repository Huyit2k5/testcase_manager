using Acme.TestCaseManagement.TestCases;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.SharedSteps;

public class SharedStepGroupManager : DomainService
{
    private readonly IRepository<SharedStepGroup, Guid> _repository;

    public SharedStepGroupManager(IRepository<SharedStepGroup, Guid> repository)
    {
        _repository = repository;
    }

    /// <summary>Builds a group with its first steps after checking that the name is free. The caller inserts it.</summary>
    public virtual async Task<SharedStepGroup> CreateAsync(string name, string? description, IReadOnlyList<TestStepInput> steps)
    {
        await EnsureNameIsFreeAsync(name, exceptId: null);

        var group = new SharedStepGroup(GuidGenerator.Create(), CurrentTenant.Id, name, description);
        group.SetSteps(steps);
        return group;
    }

    public virtual async Task ChangeNameAsync(SharedStepGroup group, string name)
    {
        var normalized = SharedStepGroup.NormalizeName(name);
        if (string.Equals(group.Name, normalized, StringComparison.Ordinal))
        {
            return;
        }

        await EnsureNameIsFreeAsync(normalized, group.Id);
        group.SetName(normalized);
    }

    /// <summary>A name names one group (ignoring case), so that a person picking from a list is never in doubt.</summary>
    public virtual async Task EnsureNameIsFreeAsync(string name, Guid? exceptId)
    {
        var normalized = SharedStepGroup.NormalizeName(name).ToLowerInvariant();
        var clash = await _repository.AnyAsync(x => x.Name.ToLower() == normalized && (exceptId == null || x.Id != exceptId));
        if (clash)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DuplicateSharedStepGroupName).WithData("Name", name.Trim());
        }
    }
}
