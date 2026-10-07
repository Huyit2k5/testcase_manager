using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Automation;

[Authorize(TestCaseManagementPermissions.ApiKeys.Default)]
public class ApiKeyAppService : TestCaseManagementAppService, IApiKeyAppService
{
    private readonly IRepository<ApiKey, Guid> _repository;
    private readonly ApiKeyManager _manager;

    public ApiKeyAppService(IRepository<ApiKey, Guid> repository, ApiKeyManager manager)
    {
        _repository = repository;
        _manager = manager;
    }

    public virtual async Task<List<ApiKeyDto>> GetListAsync()
    {
        var keys = await _repository.GetListAsync();
        var now = _manager.UtcNow;

        return keys.OrderByDescending(k => k.CreationTime).Select(k => ToDto(k, now)).ToList();
    }

    [Authorize(TestCaseManagementPermissions.ApiKeys.Manage)]
    public virtual async Task<ApiKeyCreatedDto> CreateAsync(CreateApiKeyDto input)
    {
        var (key, secret) = _manager.Create(input.Name, input.ExpiresAt);
        await _repository.InsertAsync(key, autoSave: true);

        var dto = ToDto<ApiKeyCreatedDto>(key, _manager.UtcNow);
        dto.Key = secret;
        return dto;
    }

    [Authorize(TestCaseManagementPermissions.ApiKeys.Manage)]
    public virtual async Task<ApiKeyDto> RevokeAsync(Guid id)
    {
        var key = await _repository.GetAsync(id);

        key.Revoke(_manager.UtcNow);
        await _repository.UpdateAsync(key, autoSave: true);

        return ToDto(key, _manager.UtcNow);
    }

    private static ApiKeyDto ToDto(ApiKey key, DateTime now) => ToDto<ApiKeyDto>(key, now);

    private static T ToDto<T>(ApiKey key, DateTime now)
        where T : ApiKeyDto, new()
    {
        return new T
        {
            Id = key.Id,
            Name = key.Name,
            KeyPrefix = key.KeyPrefix,
            ExpiresAt = Utc(key.ExpiresAt),
            RevokedAt = Utc(key.RevokedAt),
            LastUsedAt = Utc(key.LastUsedAt),
            CreationTime = key.CreationTime,
            CreatorId = key.CreatorId,
            IsActive = key.IsActive(now),
        };
    }

    /// <summary>Times are stored as UTC; the kind is lost on the way back from some databases, so it is stated again.</summary>
    private static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
