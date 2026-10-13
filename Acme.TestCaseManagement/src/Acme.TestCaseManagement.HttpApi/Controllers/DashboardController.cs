using Acme.TestCaseManagement.Insights;
using Acme.TestCaseManagement.Insights.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/dashboard")]
public class DashboardController : TestCaseManagementController, IDashboardAppService
{
    private readonly IDashboardAppService _dashboardAppService;

    public DashboardController(IDashboardAppService dashboardAppService)
    {
        _dashboardAppService = dashboardAppService;
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<DashboardDto> GetAsync([FromQuery] GetDashboardInput input)
    {
        return _dashboardAppService.GetAsync(input);
    }

    /// <inheritdoc />
    [HttpGet("trend")]
    public virtual Task<SprintTrendDto> GetTrendAsync([FromQuery] GetSprintTrendInput input)
    {
        return _dashboardAppService.GetTrendAsync(input);
    }

    /// <inheritdoc />
    [HttpGet("workload")]
    public virtual Task<WorkloadDto> GetWorkloadAsync([FromQuery] GetWorkloadInput input)
    {
        return _dashboardAppService.GetWorkloadAsync(input);
    }
}
