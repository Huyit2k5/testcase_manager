using Acme.TestCaseManagement.Insights.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Insights;

/// <summary>Pass rate, execution velocity, burn-down and defect density, calculated when asked (FR-025).</summary>
public interface IDashboardAppService : IApplicationService
{
    /// <summary>The metrics of one test plan, or of every run (CI runs without a plan included) when no plan is given.</summary>
    Task<DashboardDto> GetAsync(GetDashboardInput input);
}
