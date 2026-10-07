using Volo.Abp.Domain.Entities;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>A label on a test case ("smoke", "payments"). Owned by its <see cref="TestCase"/>; not part of any version.</summary>
public class TestCaseTag : Entity<Guid>
{
    public virtual Guid TestCaseId { get; protected set; }

    /// <summary>The tag as it is shown, in the spelling of whoever wrote it first.</summary>
    public virtual string Name { get; protected set; }

    /// <summary>The tag as it is compared: lower case. "Smoke" and "smoke" are one tag.</summary>
    public virtual string NormalizedName { get; protected set; }

    protected TestCaseTag()
    {
        Name = default!;
        NormalizedName = default!;
    }

    internal TestCaseTag(Guid id, Guid testCaseId, string name)
        : base(id)
    {
        TestCaseId = testCaseId;
        Name = name;
        NormalizedName = TagNames.Normalize(name);
    }
}
