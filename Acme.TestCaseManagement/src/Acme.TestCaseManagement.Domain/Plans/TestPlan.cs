using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Plans;

/// <summary>A sprint / milestone testing scope that groups test runs.</summary>
public class TestPlan : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private static readonly IReadOnlyDictionary<PlanStatus, PlanStatus[]> AllowedTransitions =
        new Dictionary<PlanStatus, PlanStatus[]>
        {
            [PlanStatus.Draft] = [PlanStatus.Active, PlanStatus.Archived],
            [PlanStatus.Active] = [PlanStatus.Completed, PlanStatus.Archived],
            [PlanStatus.Completed] = [PlanStatus.Archived],
            [PlanStatus.Archived] = [],
        };

    public virtual Guid? TenantId { get; protected set; }

    public virtual string Name { get; protected set; }

    public virtual string? Description { get; protected set; }

    /// <summary>Optional reference to a milestone owned by the host application.</summary>
    public virtual Guid? MilestoneId { get; protected set; }

    public virtual DateTime? StartDate { get; protected set; }

    public virtual DateTime? EndDate { get; protected set; }

    public virtual PlanStatus Status { get; protected set; }

    protected TestPlan()
    {
        Name = default!;
    }

    public TestPlan(
        Guid id,
        Guid? tenantId,
        string name,
        string? description = null,
        Guid? milestoneId = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
        : base(id)
    {
        TenantId = tenantId;
        Status = PlanStatus.Draft;
        Name = NormalizeName(name);
        Description = Check.Length(description, nameof(description), TestPlanConsts.MaxDescriptionLength);
        MilestoneId = milestoneId;
        ValidateDates(startDate, endDate);
        StartDate = startDate;
        EndDate = endDate;
    }

    public virtual void SetName(string name)
    {
        Name = NormalizeName(name);
    }

    public virtual void SetDescription(string? description)
    {
        Description = Check.Length(description, nameof(description), TestPlanConsts.MaxDescriptionLength);
    }

    public virtual void SetMilestone(Guid? milestoneId)
    {
        MilestoneId = milestoneId;
    }

    public virtual void SetSchedule(DateTime? startDate, DateTime? endDate)
    {
        ValidateDates(startDate, endDate);
        StartDate = startDate;
        EndDate = endDate;
    }

    public virtual void ChangeStatus(PlanStatus target)
    {
        if (!AllowedTransitions[Status].Contains(target))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidTestPlanStatusTransition)
                .WithData("From", Status)
                .WithData("To", target);
        }

        Status = target;
    }

    private static string NormalizeName(string name)
    {
        return Check.NotNullOrWhiteSpace(name, nameof(name), TestPlanConsts.MaxNameLength).Trim();
    }

    private static void ValidateDates(DateTime? startDate, DateTime? endDate)
    {
        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidTestPlanDates);
        }
    }
}
