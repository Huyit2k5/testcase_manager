using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Automation.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/api-keys")]
public class ApiKeyController : TestCaseManagementController, IApiKeyAppService
{
    private readonly IApiKeyAppService _apiKeyAppService;

    public ApiKeyController(IApiKeyAppService apiKeyAppService)
    {
        _apiKeyAppService = apiKeyAppService;
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<List<ApiKeyDto>> GetListAsync()
    {
        return _apiKeyAppService.GetListAsync();
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<ApiKeyCreatedDto> CreateAsync(CreateApiKeyDto input)
    {
        return _apiKeyAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/revoke")]
    public virtual Task<ApiKeyDto> RevokeAsync(Guid id)
    {
        return _apiKeyAppService.RevokeAsync(id);
    }
}
