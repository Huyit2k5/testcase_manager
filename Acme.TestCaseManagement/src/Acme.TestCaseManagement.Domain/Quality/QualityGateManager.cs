using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Repositories;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.Quality;

/// <summary>What a gate evaluation or sign-off applies to: exactly one of a test plan or a milestone.</summary>
public record QualityGateScope(Guid? TestPlanId, Guid? MilestoneId);

public class QualityGateManager : DomainService
{
    private readonly IRepository<QualityGate, Guid> _gateRepository;
    private readonly IRepository<TestPlan, Guid> _planRepository;
    private readonly IQualityRepository _qualityRepository;

    public QualityGateManager(
        IRepository<QualityGate, Guid> gateRepository,
        IRepository<TestPlan, Guid> planRepository,
        IQualityRepository qualityRepository)
    {
        _gateRepository = gateRepository;
        _planRepository = planRepository;
        _qualityRepository = qualityRepository;
    }

    /// <summary>
    /// Builds a gate after checking that its name is free (ignoring case). Making it the default clears the flag on
    /// the previous default. The caller inserts the returned gate.
    /// </summary>
    public virtual async Task<QualityGate> CreateAsync(
        string name,
        decimal minPassRate,
        int requiredApprovals,
        string? description,
        bool isDefault)
    {
        var gate = new QualityGate(GuidGenerator.Create(), CurrentTenant.Id, name, minPassRate, requiredApprovals, description);
        await EnsureNameIsUniqueAsync(gate.Name, exceptGateId: null);

        if (isDefault)
        {
            await ClearDefaultAsync(exceptGateId: gate.Id);
            gate.SetDefault(true);
        }

        return gate;
    }

    public virtual async Task UpdateAsync(
        QualityGate gate,
        string name,
        decimal minPassRate,
        int requiredApprovals,
        string? description,
        bool isDefault)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (!string.Equals(gate.Name, trimmed, StringComparison.Ordinal))
        {
            await EnsureNameIsUniqueAsync(trimmed, gate.Id);
            gate.SetName(trimmed);
        }

        gate.SetThresholds(minPassRate, requiredApprovals);
        gate.SetDescription(description);

        if (isDefault && !gate.IsDefault)
        {
            await ClearDefaultAsync(exceptGateId: gate.Id);
        }

        gate.SetDefault(isDefault);
    }

    /// <summary>
    /// The thresholds to use: the named gate, else the tenant's default gate, else the built-in baseline,
    /// so that an evaluation always has deterministic rules.
    /// </summary>
    public virtual async Task<QualityGateSettings> ResolveSettingsAsync(Guid? gateId)
    {
        if (gateId.HasValue)
        {
            return (await _gateRepository.GetAsync(gateId.Value)).ToSettings();
        }

        var defaultGate = await _gateRepository.FindAsync(x => x.IsDefault);
        return defaultGate?.ToSettings() ?? QualityGateSettings.Baseline;
    }

    /// <summary>Evaluates the scope against the named gate, else the default gate, else the built-in baseline.</summary>
    public virtual async Task<QualityGateEvaluation> EvaluateAsync(QualityGateScope scope, Guid? gateId)
    {
        return await EvaluateWithSettingsAsync(scope, await ResolveSettingsAsync(gateId));
    }

    /// <summary>Evaluates the scope against explicit settings, e.g. the thresholds frozen in a sign-off report.</summary>
    public virtual async Task<QualityGateEvaluation> EvaluateWithSettingsAsync(QualityGateScope scope, QualityGateSettings settings)
    {
        var plans = await ResolvePlansAsync(scope);

        var data = await _qualityRepository.GetScopeDataAsync(plans.Select(p => p.Id).ToList());
        var metrics = QualityMetricsCalculator.Calculate(data.RunCount, data.Items, data.OpenDefects);
        var criteria = QualityGateEvaluator.Evaluate(settings, metrics);

        return new QualityGateEvaluation(
            criteria.All(c => c.Passed),
            settings,
            new QualityGateScopeInfo(
                scope.TestPlanId,
                scope.MilestoneId,
                plans.Select(p => new ScopePlan(p.Id, p.Name)).ToList()),
            metrics,
            criteria,
            Clock.Now);
    }

    /// <summary>The plan, or all plans of the milestone. Exactly one of the two must be given.</summary>
    public virtual async Task<List<TestPlan>> ResolvePlansAsync(QualityGateScope scope)
    {
        if (scope.TestPlanId.HasValue == scope.MilestoneId.HasValue)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidSignOffScope);
        }

        if (scope.TestPlanId.HasValue)
        {
            var plan = await _planRepository.FindAsync(scope.TestPlanId.Value);
            if (plan == null)
            {
                throw new BusinessException(TestCaseManagementErrorCodes.TestPlanNotFound)
                    .WithData("PlanId", scope.TestPlanId.Value);
            }

            return new List<TestPlan> { plan };
        }

        var milestoneId = scope.MilestoneId!.Value;
        var plans = await _planRepository.GetListAsync(x => x.MilestoneId == milestoneId);
        if (plans.Count == 0)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SignOffScopeEmpty)
                .WithData("MilestoneId", milestoneId);
        }

        return plans.OrderBy(p => p.Name).ToList();
    }

    protected virtual async Task EnsureNameIsUniqueAsync(string name, Guid? exceptGateId)
    {
        var lowered = name.ToLowerInvariant();
        var existing = await _gateRepository.FindAsync(x => x.Name.ToLower() == lowered);

        if (existing != null && existing.Id != exceptGateId)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DuplicateQualityGateName).WithData("Name", name);
        }
    }

    protected virtual async Task ClearDefaultAsync(Guid exceptGateId)
    {
        var defaults = await _gateRepository.GetListAsync(x => x.IsDefault && x.Id != exceptGateId);
        if (defaults.Count == 0)
        {
            return;
        }

        foreach (var gate in defaults)
        {
            gate.SetDefault(false);
        }

        await _gateRepository.UpdateManyAsync(defaults, autoSave: true);
    }
}
