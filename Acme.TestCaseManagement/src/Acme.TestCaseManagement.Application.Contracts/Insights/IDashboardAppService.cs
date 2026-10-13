using Acme.TestCaseManagement.Insights.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Insights;

/// <summary>Pass rate, execution velocity, burn-down and defect density, calculated when asked (FR-025).</summary>
public interface IDashboardAppService : IApplicationService
{
    /// <summary>The metrics of one test plan, or of every run (CI runs without a plan included) when no plan is given.</summary>
    Task<DashboardDto> GetAsync(GetDashboardInput input);

    /// <summary>
    /// How the quality moved from one test plan ("sprint") to the next: the latest plans that have run items, oldest first, each with
    /// its pass rate, first-time pass rate, completion and defects.
    /// </summary>
    Task<SprintTrendDto> GetTrendAsync(GetSprintTrendInput input);

    /// <summary>
    /// How the work is spread over the testers: for each person the run items given to them and the attempts they made inside the
    /// window. Needs the permission to manage plans, as assigning the work does.
    /// </summary>
    Task<WorkloadDto> GetWorkloadAsync(GetWorkloadInput input);
}
