using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
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

    public ApiKeyValidator(
        ApiKeyManager manager,
        IRepository<ApiKey, Guid> repository,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<ApiKeyValidator> logger)
    {
        _manager = manager;
        _repository = repository;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public virtual async Task<ApiKeyIdentity?> ValidateAsync(string secret)
    {
        // Authentication runs before the unit of work of the request exists, so this makes its own.
        using var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true);

        var key = await _manager.FindActiveAsync(secret);
        if (key == null)
        {
            await unitOfWork.CompleteAsync();
            return null;
        }

        var now = _manager.UtcNow;
        if (key.LastUsedAt == null || now - key.LastUsedAt.Value > LastUsedGranularity)
        {
            try
            {
                key.MarkUsed(now);
                await _repository.UpdateAsync(key, autoSave: true);
            }
            catch (Exception exception)
            {
                // Parallel requests of one pipeline can update the same row at the same moment. The time of last use is
                // information, never a reason to refuse a request that carries a valid key.
                _logger.LogDebug(exception, "The last use of API key {KeyPrefix} could not be recorded.", key.KeyPrefix);
            }
        }

        await unitOfWork.CompleteAsync();
        return new ApiKeyIdentity(key.Id, key.TenantId, key.Name);
    }
}
