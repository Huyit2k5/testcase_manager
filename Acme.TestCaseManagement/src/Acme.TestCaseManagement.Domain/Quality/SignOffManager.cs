using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Users;

namespace Acme.TestCaseManagement.Quality;

public class SignOffManager : DomainService
{
    private readonly QualityGateManager _gateManager;
    private readonly IRepository<SignOffReport, Guid> _reportRepository;
    private readonly ICurrentUser _currentUser;

    public SignOffManager(
        QualityGateManager gateManager,
        IRepository<SignOffReport, Guid> reportRepository,
        ICurrentUser currentUser)
    {
        _gateManager = gateManager;
        _reportRepository = reportRepository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Evaluates the gate for the scope. When it passes, builds a Pending report that freezes the evaluation and holds the
    /// current user's approval (which already completes it when one approval is enough). A Pending report of the same
    /// scope is superseded. The caller inserts the returned report. When the gate fails nothing is created and
    /// <see cref="QualityGateNotPassedException"/> carries the breakdown.
    /// </summary>
    public virtual async Task<SignOffReport> StartAsync(
        QualityGateScope scope, Guid? gateId, string? title, string? approverRole, string? comment)
    {
        var (userId, userName) = RequireUser();

        var evaluation = await _gateManager.EvaluateAsync(scope, gateId);
        if (!evaluation.Passed)
        {
            throw new QualityGateNotPassedException(evaluation);
        }

        await SupersedePendingAsync(scope);

        var now = Clock.Now;
        var json = SignOffSnapshot.Serialize(new SignOffSnapshot(SignOffSnapshot.CurrentSchemaVersion, now, evaluation));
        var report = new SignOffReport(
            GuidGenerator.Create(), CurrentTenant.Id, scope, BuildTitle(title, evaluation), evaluation.Gate, json);

        if (report.ProjectId == Guid.Empty)
        {
            // A plan (or the plans of a milestone in one project) fixes the project of the report.
            report.SetProject((await _gateManager.ResolvePlansAsync(scope)).First().ProjectId);
        }

        report.AddApproval(GuidGenerator.Create(), userId, userName, approverRole, comment, now);
        return report;
    }

    /// <summary>
    /// Adds the current user's approval. The gate is evaluated again with the thresholds frozen in the report, and the
    /// approval is refused when it would fail now (for example a Critical defect was linked after the first signature).
    /// </summary>
    public virtual async Task<SignOffApproval> ApproveAsync(SignOffReport report, string? approverRole, string? comment)
    {
        var (userId, userName) = RequireUser();
        report.EnsureCanBeApprovedBy(userId);

        var evaluation = await _gateManager.EvaluateWithSettingsAsync(report.ToScope(), report.ToSettings());
        if (!evaluation.Passed)
        {
            throw new QualityGateNotPassedException(evaluation);
        }

        return report.AddApproval(GuidGenerator.Create(), userId, userName, approverRole, comment, Clock.Now);
    }

    protected virtual async Task SupersedePendingAsync(QualityGateScope scope)
    {
        var pending = await _reportRepository.GetListAsync(x =>
            x.Status == SignOffStatus.Pending && x.TestPlanId == scope.TestPlanId && x.MilestoneId == scope.MilestoneId
            && (scope.ProjectId == null || x.ProjectId == scope.ProjectId));

        foreach (var report in pending)
        {
            report.Supersede();
        }

        if (pending.Count > 0)
        {
            await _reportRepository.UpdateManyAsync(pending);
        }
    }

    private (Guid UserId, string UserName) RequireUser()
    {
        if (!_currentUser.Id.HasValue)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SignOffRequiresUser);
        }

        var id = _currentUser.Id.Value;
        return (id, _currentUser.UserName ?? _currentUser.Email ?? id.ToString());
    }

    private static string BuildTitle(string? title, QualityGateEvaluation evaluation)
    {
        var text = string.IsNullOrWhiteSpace(title)
            ? "Sign-off: " + string.Join(", ", evaluation.Scope.Plans.Select(p => p.Name))
            : title.Trim();

        return text.Length <= SignOffConsts.MaxTitleLength ? text : text[..SignOffConsts.MaxTitleLength];
    }
}
