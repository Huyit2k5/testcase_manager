using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.Quality;

namespace Acme.TestCaseManagement.QualityGates;

/// <summary>Turns the domain evaluation records into API DTOs. Shared by gate evaluation and sign-off reports.</summary>
internal static class QualityGateDtoFactory
{
    /// <param name="evaluation">The domain evaluation to convert.</param>
    /// <param name="label">Resolves the localized name of a criterion code.</param>
    public static QualityGateEvaluationDto ToDto(QualityGateEvaluation evaluation, Func<string, string> label)
    {
        var m = evaluation.Metrics;

        return new QualityGateEvaluationDto
        {
            Passed = evaluation.Passed,
            EvaluatedTime = evaluation.EvaluatedTime,
            Gate = new QualityGateSettingsDto
            {
                Id = evaluation.Gate.Id,
                Name = evaluation.Gate.Name,
                MinPassRate = evaluation.Gate.MinPassRate,
                RequiredApprovals = evaluation.Gate.RequiredApprovals,
                IsBuiltIn = evaluation.Gate.IsBuiltIn,
            },
            Scope = new QualityGateScopeDto
            {
                TestPlanId = evaluation.Scope.TestPlanId,
                MilestoneId = evaluation.Scope.MilestoneId,
                Plans = evaluation.Scope.Plans.Select(p => new ScopePlanDto { Id = p.Id, Name = p.Name }).ToList(),
            },
            Metrics = new QualityMetricsDto
            {
                RunCount = m.RunCount,
                TotalItems = m.TotalItems,
                Passed = m.Passed,
                Failed = m.Failed,
                Blocked = m.Blocked,
                Skipped = m.Skipped,
                Untested = m.Untested,
                CompletionPercentage = m.CompletionPercentage,
                PassRate = m.PassRate,
                FirstTimePassRate = m.FirstTimePassRate,
                P1Total = m.P1Total,
                P1Executed = m.P1Executed,
                P1ExecutionRate = m.P1ExecutionRate,
                OpenDefects = new OpenDefectCountsDto
                {
                    Critical = m.OpenDefects.Critical,
                    High = m.OpenDefects.High,
                    Medium = m.OpenDefects.Medium,
                    Low = m.OpenDefects.Low,
                    Total = m.OpenDefects.Total,
                },
                OpenDefectIssues = m.OpenDefectIssues.Select(d => new OpenDefectDto
                {
                    ExternalSystem = d.ExternalSystem,
                    IssueKey = d.IssueKey,
                    IssueUrl = d.IssueUrl,
                    Severity = d.Severity,
                }).ToList(),
            },
            Criteria = evaluation.Criteria.Select(c => new GateCriterionResultDto
            {
                Code = c.Code,
                Label = label(c.Code),
                Operator = c.Operator,
                Threshold = c.Threshold,
                Actual = c.Actual,
                Passed = c.Passed,
            }).ToList(),
        };
    }
}
