using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Suites;

/// <summary>A folder in the hierarchical test library. <see cref="ParentId"/> is null for root suites.</summary>
public class TestSuite : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>The project it belongs to. Guid.Empty only for data made before projects existed, until it is given to the default project.</summary>
    public virtual Guid ProjectId { get; protected set; }

    public virtual Guid? ParentId { get; protected set; }

    public virtual string Name { get; protected set; }

    public virtual string? Description { get; protected set; }

    /// <summary>Zero-based position among siblings that share the same parent.</summary>
    public virtual int Order { get; protected set; }

    protected TestSuite()
    {
        Name = default!;
    }

    public TestSuite(Guid id, Guid? tenantId, string name, Guid? parentId, int order, string? description = null)
        : base(id)
    {
        TenantId = tenantId;
        ParentId = parentId;
        Order = order;
        Name = NormalizeName(name);
        SetDescription(description);
    }

    public virtual void SetName(string name)
    {
        Name = NormalizeName(name);
    }

    private static string NormalizeName(string name)
    {
        return Check.NotNullOrWhiteSpace(name, nameof(name), TestSuiteConsts.MaxNameLength).Trim();
    }

    public virtual void SetDescription(string? description)
    {
        Description = Check.Length(description, nameof(description), TestSuiteConsts.MaxDescriptionLength);
    }

    /// <summary>Hierarchy changes go through <see cref="TestSuiteManager.MoveAsync"/>, which prevents cycles.</summary>
    /// <summary>Used when it is created (and to give old data its project); the managers keep it consistent, so it is not changed afterwards.</summary>
    public virtual void SetProject(Guid projectId)
    {
        ProjectId = projectId;
    }

    internal void MoveTo(Guid? parentId, int order)
    {
        ParentId = parentId;
        Order = order;
    }

    internal void SetOrder(int order)
    {
        Order = order;
    }
}
