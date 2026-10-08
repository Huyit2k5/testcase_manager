using System.Security.Claims;
using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Xunit;

namespace Acme.TestCaseManagement;

public class ApiKey_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ApiKeyManager _manager;
    private readonly IApiKeyAppService _service;
    private readonly IApiKeyValidator _validator;
    private readonly IRepository<ApiKey, Guid> _repository;

    public ApiKey_Tests()
    {
        _manager = GetRequiredService<ApiKeyManager>();
        _service = GetRequiredService<IApiKeyAppService>();
        _validator = GetRequiredService<IApiKeyValidator>();
        _repository = GetRequiredService<IRepository<ApiKey, Guid>>();
    }

    // ---- the manager

    [Fact]
    public void A_Created_Key_Has_A_Recognizable_Shape_And_Only_Its_Hash_Is_Kept()
    {
        var (key, secret) = _manager.Create("GitHub Actions", expiresAt: null);

        secret.ShouldMatch(@"^tcm_[0-9a-f]{8}_[A-Za-z0-9_-]{43}$");
        key.KeyPrefix.ShouldBe(secret[..12]);
        key.KeyHash.ShouldBe(ApiKeyManager.Hash(secret));
        key.KeyHash.Length.ShouldBe(64);
        key.KeyHash.ShouldNotContain(secret[13..]);
        key.Name.ShouldBe("GitHub Actions");
        key.IsActive(DateTime.UtcNow).ShouldBeTrue();
    }

    [Fact]
    public void Two_Keys_Never_Share_A_Secret()
    {
        var secrets = Enumerable.Range(0, 50).Select(_ => _manager.Create("k", null).Secret).ToList();

        secrets.Distinct().Count().ShouldBe(50);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tcm_")]
    [InlineData("tcm_12345678")]
    [InlineData("xyz_12345678_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("tcm_12345678-abcdefghijklmnopqrstuvwxyz")]
    public void Text_That_Is_Not_The_Shape_Of_A_Key_Has_No_Prefix(string? text)
    {
        ApiKeyManager.TryGetPrefix(text, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_Working_Key_Is_Found_By_Its_Secret_And_A_Changed_Secret_Is_Not()
    {
        var (key, secret) = _manager.Create("CI", null);
        await _repository.InsertAsync(key, autoSave: true);

        (await _manager.FindActiveAsync(secret))!.Id.ShouldBe(key.Id);

        // The last character, the identifying prefix and the length each make it another secret.
        var changedLast = secret[..^1] + (secret[^1] == 'A' ? 'B' : 'A');
        (await _manager.FindActiveAsync(changedLast)).ShouldBeNull();
        (await _manager.FindActiveAsync(secret + "x")).ShouldBeNull();
        (await _manager.FindActiveAsync(secret[..^1])).ShouldBeNull();
        (await _manager.FindActiveAsync("tcm_00000000_" + secret[13..])).ShouldBeNull();
        (await _manager.FindActiveAsync(null)).ShouldBeNull();
    }

    [Fact]
    public async Task A_Revoked_Key_And_An_Expired_Key_Stop_Working()
    {
        var (revoked, revokedSecret) = _manager.Create("revoked", null);
        await _repository.InsertAsync(revoked, autoSave: true);
        (await _manager.FindActiveAsync(revokedSecret)).ShouldNotBeNull();

        revoked.Revoke(_manager.UtcNow);
        await _repository.UpdateAsync(revoked, autoSave: true);
        (await _manager.FindActiveAsync(revokedSecret)).ShouldBeNull();

        // An expired key cannot be created, so one is built as the database would hold it after the time has passed.
        var expiredSecret = "tcm_abcdef12_" + new string('e', 43);
        await _repository.InsertAsync(
            new ApiKey(Guid.NewGuid(), null, "expired", expiredSecret[..12], ApiKeyManager.Hash(expiredSecret), DateTime.UtcNow.AddMinutes(-1)),
            autoSave: true);
        (await _manager.FindActiveAsync(expiredSecret)).ShouldBeNull();
    }

    [Fact]
    public void An_Expiry_Must_Be_In_The_Future_And_A_Time_Without_A_Kind_Is_Taken_As_UTC()
    {
        Should.Throw<BusinessException>(() => _manager.Create("past", DateTime.UtcNow.AddMinutes(-1)))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidApiKeyExpiry);

        var unspecified = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Unspecified);
        var (key, _) = _manager.Create("future", unspecified);
        key.ExpiresAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        key.ExpiresAt.Value.ShouldBe(unspecified, tolerance: TimeSpan.FromSeconds(1));
    }

    // ---- the validator

    [Fact]
    public async Task The_Validator_Tells_Who_The_Key_Is_And_Notes_When_It_Was_Used()
    {
        var created = await _service.CreateAsync(new CreateApiKeyDto { Name = "Nightly" });

        var identity = await _validator.ValidateAsync(created.Key);

        identity.ShouldNotBeNull();
        identity.Id.ShouldBe(created.Id);
        identity.Name.ShouldBe("Nightly");
        (await _validator.ValidateAsync(created.Key + "x")).ShouldBeNull();

        var listed = (await _service.GetListAsync()).Single();
        listed.LastUsedAt.ShouldNotBeNull();
        listed.LastUsedAt!.Value.ShouldBe(DateTime.UtcNow, tolerance: TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task The_Last_Use_Is_Written_At_Most_Once_A_Minute()
    {
        var created = await _service.CreateAsync(new CreateApiKeyDto { Name = "Busy" });

        await _validator.ValidateAsync(created.Key);
        var first = (await _service.GetListAsync()).Single().LastUsedAt;
        await Task.Delay(50);
        await _validator.ValidateAsync(created.Key);

        (await _service.GetListAsync()).Single().LastUsedAt.ShouldBe(first);
    }

    // ---- the application service

    [Fact]
    public async Task Creating_A_Key_Returns_The_Secret_Once_And_Listing_Never_Does()
    {
        var created = await _service.CreateAsync(new CreateApiKeyDto { Name = "GitHub Actions - main", ExpiresAt = DateTime.UtcNow.AddDays(30) });

        created.Key.ShouldStartWith("tcm_");
        created.KeyPrefix.ShouldBe(created.Key[..12]);
        created.IsActive.ShouldBeTrue();
        created.ExpiresAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);

        var listed = await _service.GetListAsync();
        listed.Single().Id.ShouldBe(created.Id);
        listed.Single().KeyPrefix.ShouldBe(created.KeyPrefix);
        typeof(ApiKeyDto).GetProperties().Select(p => p.Name).ShouldNotContain("Key");

        var stored = await _repository.GetAsync(created.Id);
        stored.KeyHash.ShouldNotBe(created.Key);
        stored.KeyHash.ShouldBe(ApiKeyManager.Hash(created.Key));
    }

    [Fact]
    public async Task Revoking_Stops_The_Key_At_Once_And_Revoking_Again_Changes_Nothing()
    {
        var created = await _service.CreateAsync(new CreateApiKeyDto { Name = "Leaked" });
        (await _validator.ValidateAsync(created.Key)).ShouldNotBeNull();

        var revoked = await _service.RevokeAsync(created.Id);
        revoked.IsActive.ShouldBeFalse();
        revoked.RevokedAt.ShouldNotBeNull();
        (await _validator.ValidateAsync(created.Key)).ShouldBeNull();

        var again = await _service.RevokeAsync(created.Id);
        // The same moment; a database may keep fewer decimals of a second than .NET (MySQL keeps six, .NET seven).
        again.RevokedAt!.Value.ShouldBe(revoked.RevokedAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task The_List_Is_Newest_First_And_Keeps_Revoked_And_Expired_Keys()
    {
        var first = await _service.CreateAsync(new CreateApiKeyDto { Name = "first" });
        await Task.Delay(20);
        var second = await _service.CreateAsync(new CreateApiKeyDto { Name = "second" });
        await _service.RevokeAsync(first.Id);

        var listed = await _service.GetListAsync();

        listed.Select(k => k.Name).ShouldBe(new[] { "second", "first" });
        listed.Select(k => k.IsActive).ShouldBe(new[] { true, false });
        second.Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public async Task An_Unknown_Key_Cannot_Be_Revoked()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.RevokeAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_Past_Expiry_Is_Refused_By_The_Service_Too()
    {
        var exception = await Should.ThrowAsync<BusinessException>(
            () => _service.CreateAsync(new CreateApiKeyDto { Name = "late", ExpiresAt = DateTime.UtcNow.AddHours(-1) }));

        exception.Code.ShouldBe(TestCaseManagementErrorCodes.InvalidApiKeyExpiry);
    }

    // ---- the permission of a key

    [Fact]
    public async Task A_Key_Is_Granted_The_Publish_Permission_And_Nothing_Else()
    {
        var provider = new ApiKeyPermissionValueProvider(Substitute.For<IPermissionStore>());
        var definitions = GetRequiredService<IPermissionDefinitionManager>();
        var apiKey = Principal(authenticated: true, ApiKeyClaimTypes.ApiKeyId);

        var granted = new List<string>();
        foreach (var name in TestCaseManagementPermissions.GetAll().Where(n => n != TestCaseManagementPermissions.GroupName))
        {
            var definition = await definitions.GetAsync(name);
            if (await provider.CheckAsync(new PermissionValueCheckContext(definition, apiKey)) == PermissionGrantResult.Granted)
            {
                granted.Add(name);
            }
        }

        granted.ShouldBe(new[] { TestCaseManagementPermissions.AutomationResults.Publish });
    }

    [Fact]
    public async Task Only_A_Principal_Authenticated_As_An_Api_Key_Gets_The_Permission()
    {
        var provider = new ApiKeyPermissionValueProvider(Substitute.For<IPermissionStore>());
        var publish = await GetRequiredService<IPermissionDefinitionManager>().GetAsync(TestCaseManagementPermissions.AutomationResults.Publish);

        async Task<PermissionGrantResult> Check(ClaimsPrincipal? principal) =>
            await provider.CheckAsync(new PermissionValueCheckContext(publish, principal));

        (await Check(Principal(authenticated: true, ApiKeyClaimTypes.ApiKeyId))).ShouldBe(PermissionGrantResult.Granted);
        (await Check(Principal(authenticated: true, AbpClaimTypes.UserId))).ShouldBe(PermissionGrantResult.Undefined); // a user
        (await Check(Principal(authenticated: false, ApiKeyClaimTypes.ApiKeyId))).ShouldBe(PermissionGrantResult.Undefined); // not authenticated
        (await Check(null)).ShouldBe(PermissionGrantResult.Undefined);
    }

    [Fact]
    public async Task The_Bulk_Check_Agrees_With_The_Single_Check()
    {
        var provider = new ApiKeyPermissionValueProvider(Substitute.For<IPermissionStore>());
        var definitions = GetRequiredService<IPermissionDefinitionManager>();
        var all = (await definitions.GetPermissionsAsync()).Where(p => p.Name.StartsWith(TestCaseManagementPermissions.GroupName + ".")).ToList();

        var result = await provider.CheckAsync(new PermissionValuesCheckContext(all, Principal(authenticated: true, ApiKeyClaimTypes.ApiKeyId)));

        result.Result.Where(r => r.Value == PermissionGrantResult.Granted).Select(r => r.Key)
            .ShouldBe(new[] { TestCaseManagementPermissions.AutomationResults.Publish });
        result.Result.Count.ShouldBe(all.Count);
    }

    [Fact]
    public async Task The_New_Permissions_Are_Defined_And_Localized_In_Both_Languages()
    {
        var definitions = GetRequiredService<IPermissionDefinitionManager>();

        foreach (var name in new[]
                 {
                     TestCaseManagementPermissions.ApiKeys.Default, TestCaseManagementPermissions.ApiKeys.Manage,
                     TestCaseManagementPermissions.AutomationResults.Default, TestCaseManagementPermissions.AutomationResults.Publish,
                 })
        {
            (await definitions.GetOrNullAsync(name)).ShouldNotBeNull(name);
        }
    }

    private static ClaimsPrincipal Principal(bool authenticated, string claimType)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(claimType, Guid.NewGuid().ToString()) }, authenticated ? "Test" : null);
        return new ClaimsPrincipal(identity);
    }
}
