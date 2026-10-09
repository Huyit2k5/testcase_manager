using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Projects;

/// <summary>
/// A project is the top level of the library: its suites (and so its test cases), plans, requirements and runs belong to it and are
/// not mixed with those of another project. The key is a short code in capitals ("EINV") that does not change once chosen.
/// </summary>
public class Project : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual string Key { get; protected set; }

    public virtual string Name { get; protected set; }

    public virtual string? Description { get; protected set; }

    /// <summary>An archived project is kept for reading, and nothing can be added to it.</summary>
    public virtual bool IsArchived { get; protected set; }

    protected Project()
    {
        Key = default!;
        Name = default!;
    }

    internal Project(Guid id, Guid? tenantId, string key, string name, string? description)
        : base(id)
    {
        TenantId = tenantId;
        Key = key;
        Name = NormalizeName(name);
        SetDescription(description);
    }

    public virtual void SetName(string name)
    {
        Name = NormalizeName(name);
    }

    private static string NormalizeName(string name)
    {
        return Check.NotNullOrWhiteSpace(name, nameof(name), ProjectConsts.MaxNameLength).Trim();
    }

    public virtual void SetDescription(string? description)
    {
        Description = Check.Length(description, nameof(description), ProjectConsts.MaxDescriptionLength);
    }

    public virtual void Archive()
    {
        IsArchived = true;
    }

    public virtual void Restore()
    {
        IsArchived = false;
    }
}
