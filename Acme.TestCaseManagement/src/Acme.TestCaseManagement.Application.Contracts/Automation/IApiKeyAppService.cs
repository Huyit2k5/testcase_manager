using Acme.TestCaseManagement.Automation.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Automation;

/// <summary>The API keys that let a CI/CD pipeline publish automated results.</summary>
public interface IApiKeyAppService : IApplicationService
{
    /// <summary>Every key of the tenant, newest first, revoked and expired ones included. Secrets are never returned.</summary>
    Task<List<ApiKeyDto>> GetListAsync();

    /// <summary>
    /// Creates a key. The answer holds the secret, which is shown only now: only a hash of it is stored. A key can publish
    /// automation results and do nothing else.
    /// </summary>
    Task<ApiKeyCreatedDto> CreateAsync(CreateApiKeyDto input);

    /// <summary>Stops the key from working, at once and for good. Revoking a revoked key changes nothing.</summary>
    Task<ApiKeyDto> RevokeAsync(Guid id);
}
