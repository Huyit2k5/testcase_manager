using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Requirements;

/// <summary>A business requirement or user story that test cases are traced to.</summary>
public class Requirement : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Unique within a tenant, e.g. <c>REQ-AUTH-01</c>.</summary>
    /// <summary>The project it belongs to. Guid.Empty only for data made before projects existed, until it is given to the default project.</summary>
    public virtual Guid ProjectId { get; protected set; }

    public virtual string Code { get; protected set; }

    public virtual string Title { get; protected set; }

    public virtual string? Description { get; protected set; }

    public virtual string? AcceptanceCriteria { get; protected set; }

    public virtual PriorityLevel Priority { get; protected set; }

    /// <summary>Optional reference to a milestone owned by the host application.</summary>
    public virtual Guid? MilestoneId { get; protected set; }

    protected Requirement()
    {
        Code = default!;
        Title = default!;
    }

    public Requirement(Guid id, Guid? tenantId, string code, string title)
        : base(id)
    {
        TenantId = tenantId;
        Priority = PriorityLevel.Medium;
        Code = NormalizeCode(code);
        Title = NormalizeTitle(title);
    }

    /// <summary>Used when it is created (and to give old data its project); the managers keep it consistent, so it is not changed afterwards.</summary>
    public virtual void SetProject(Guid projectId)
    {
        ProjectId = projectId;
    }

    public virtual void SetCode(string code)
    {
        Code = NormalizeCode(code);
    }

    public virtual void SetTitle(string title)
    {
        Title = NormalizeTitle(title);
    }

    public virtual void SetDetails(
        string? description, string? acceptanceCriteria, PriorityLevel priority, Guid? milestoneId)
    {
        Description = Check.Length(description, nameof(description), RequirementConsts.MaxTextLength);
        AcceptanceCriteria = Check.Length(acceptanceCriteria, nameof(acceptanceCriteria), RequirementConsts.MaxTextLength);
        Priority = priority;
        MilestoneId = milestoneId;
    }

    private static string NormalizeCode(string code)
    {
        return Check.NotNullOrWhiteSpace(code, nameof(code), RequirementConsts.MaxCodeLength).Trim();
    }

    private static string NormalizeTitle(string title)
    {
        return Check.NotNullOrWhiteSpace(title, nameof(title), RequirementConsts.MaxTitleLength).Trim();
    }
}
