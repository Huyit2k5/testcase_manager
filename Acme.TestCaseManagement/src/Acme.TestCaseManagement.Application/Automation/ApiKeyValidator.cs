using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Acme.TestCaseManagement.Automation;

public class ApiKeyValidator : IApiKeyValidator, ITransientDependency
{
    /// <summary>The last-use time is written at most this often, so that a busy pipeline does not write on every request.</summary>
    private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(1);

    private readonly ApiKeyManager _manager;
    private readonly IRepository<ApiKey, Guid> _repository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<ApiKeyValidator> _logger;
    private readonly ICurrentTenant _currentTenant;

    public ApiKeyValidator(
        ApiKeyManager manager,
        IRepository<ApiKey, Guid> repository,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<ApiKeyValidator> logger,
        ICurrentTenant currentTenant)
    {
        _manager = manager;
        _repository = repository;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
        _currentTenant = currentTenant;
    }

    public virtual async Task<ApiKeyIdentity?> ValidateAsync(string secret)
    {
        // Authentication runs before the unit of work of the request exists, so this makes its own.
        ApiKeyIdentity? identity = null;
        var markUsed = false;
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var key = await _manager.FindActiveAsync(secret);
            if (key != null)
            {
                identity = new ApiKeyIdentity(key.Id, key.TenantId, key.Name);
                markUsed = key.LastUsedAt == null || _manager.UtcNow - key.LastUsedAt.Value > LastUsedGranularity;
            }

            await unitOfWork.CompleteAsync();
        }

        if (identity != null && markUsed)
        {
            await MarkUsedAsync(identity);
        }

        return identity;
    }

    /// <summary>
    /// Parallel requests of one pipeline can update the same row at the same moment. The time of last use is information,
    /// never a reason to refuse a request that carries a valid key, so it is written in a unit of work of its own: a
    /// failure there is dropped together with that unit of work, and cannot reach the one that answered the request.
    /// </summary>
    private async Task MarkUsedAsync(ApiKeyIdentity identity)
    {
        try
        {
            using var tenant = _currentTenant.Change(identity.TenantId);
            using var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true);
            var key = await _repository.FindAsync(identity.Id);
            if (key != null)
            {
                key.MarkUsed(_manager.UtcNow);
                await _repository.UpdateAsync(key, autoSave: true);
            }

            await unitOfWork.CompleteAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The last use of API key {KeyId} could not be recorded.", identity.Id);
        }
    }
}
