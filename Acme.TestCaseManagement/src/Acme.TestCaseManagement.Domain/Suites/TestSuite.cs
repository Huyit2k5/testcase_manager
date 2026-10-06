using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Suites;

/// <summary>A folder in the hierarchical test library. <see cref="ParentId"/> is null for root suites.</summary>
public class TestSuite : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

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
