using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Quality;

/// <summary>
/// A named set of release thresholds. Only the pass rate and the number of approvals are configurable; the
/// P1 and open Critical/High defect rules are fixed by constitution V (see <see cref="QualityGateEvaluator"/>).
/// </summary>
public class QualityGate : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Unique within a tenant (ignoring case), e.g. "Release" or "Hotfix".</summary>
    public virtual string Name { get; protected set; }

    public virtual string? Description { get; protected set; }

    /// <summary>Minimum pass rate in percent, greater than 0 and at most 100.</summary>
    public virtual decimal MinPassRate { get; protected set; }

    /// <summary>Distinct approvers a sign-off needs before it becomes Approved.</summary>
    public virtual int RequiredApprovals { get; protected set; }

    /// <summary>The gate used when a sign-off or evaluation does not name one. At most one per tenant.</summary>
    public virtual bool IsDefault { get; protected set; }

    protected QualityGate()
    {
        Name = default!;
    }

    public QualityGate(
        Guid id,
        Guid? tenantId,
        string name,
        decimal minPassRate,
        int requiredApprovals,
        string? description = null)
        : base(id)
    {
        TenantId = tenantId;
        Name = NormalizeName(name);
        Description = Check.Length(description, nameof(description), QualityGateConsts.MaxDescriptionLength);
        (MinPassRate, RequiredApprovals) = ValidateThresholds(minPassRate, requiredApprovals);
    }

    public virtual void SetName(string name)
    {
        Name = NormalizeName(name);
    }

    public virtual void SetDescription(string? description)
    {
        Description = Check.Length(description, nameof(description), QualityGateConsts.MaxDescriptionLength);
    }

    public virtual void SetThresholds(decimal minPassRate, int requiredApprovals)
    {
        (MinPassRate, RequiredApprovals) = ValidateThresholds(minPassRate, requiredApprovals);
    }

    public virtual QualityGateSettings ToSettings()
    {
        return new QualityGateSettings(Id, Name, MinPassRate, RequiredApprovals, false);
    }

    /// <summary>The single-default rule is kept by <see cref="QualityGateManager"/>.</summary>
    internal void SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
    }

    private static string NormalizeName(string name)
    {
        return Check.NotNullOrWhiteSpace(name, nameof(name), QualityGateConsts.MaxNameLength).Trim();
    }

    private static (decimal MinPassRate, int RequiredApprovals) ValidateThresholds(decimal minPassRate, int requiredApprovals)
    {
        // 0 would switch the pass rate criterion off, which the constitution does not allow a gate to do.
        if (minPassRate <= 0m || minPassRate > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(minPassRate), minPassRate, "The minimum pass rate must be above 0 and at most 100.");
        }

        if (requiredApprovals < 1 || requiredApprovals > QualityGateConsts.MaxRequiredApprovals)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredApprovals), requiredApprovals, $"Required approvals must be between 1 and {QualityGateConsts.MaxRequiredApprovals}.");
        }

        return (minPassRate, requiredApprovals);
    }
}
