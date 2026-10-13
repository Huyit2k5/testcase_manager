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

    /// <summary>
    /// False (the default, and what every project made before members existed is): everyone who may use the module sees the project.
    /// True: only its members, and the people who manage projects, see it and what is in it.
    /// </summary>
    public virtual bool IsRestricted { get; protected set; }

    /// <summary>
    /// The last number given to a test case code made for this project ("EINV-007" is 7). It only goes up, so a number is not used
    /// twice even after the test case that had it was deleted.
    /// </summary>
    public virtual int LastTestCaseNumber { get; protected set; }

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

    /// <summary>The next code for a test case of this project: the key and a number of at least three digits ("EINV-008").</summary>
    public virtual string TakeNextTestCaseCode()
    {
        LastTestCaseNumber++;
        return Key + "-" + LastTestCaseNumber.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
    }

    public virtual void SetRestricted(bool restricted)
    {
        IsRestricted = restricted;
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
