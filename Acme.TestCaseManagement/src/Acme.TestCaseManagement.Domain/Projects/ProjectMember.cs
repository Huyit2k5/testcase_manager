using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Projects;

/// <summary>
/// A person who may see a restricted project and what is in it. The module has no users of its own, so this holds the id that the
/// host knows; there is no foreign key. It only counts while <see cref="Project.IsRestricted"/> is true.
/// </summary>
public class ProjectMember : CreationAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid ProjectId { get; protected set; }

    public virtual Guid UserId { get; protected set; }

    protected ProjectMember()
    {
    }

    public ProjectMember(Guid id, Guid? tenantId, Guid projectId, Guid userId)
        : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        UserId = userId;
    }
}
