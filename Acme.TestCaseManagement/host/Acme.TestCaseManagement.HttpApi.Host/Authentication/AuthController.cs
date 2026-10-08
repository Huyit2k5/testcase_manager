using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Identity;

namespace Acme.TestCaseManagement.Authentication;

public class LoginInput
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResult
{
    public string AccessToken { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public string UserName { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = new();
}

/// <summary>Sign-in for the sample host: checks the ABP Identity password and returns a bearer token.</summary>
[RemoteService(Name = "Auth")]
[Route("api/auth")]
public class AuthController : AbpControllerBase
{
    private readonly IdentityUserManager _users;
    private readonly JwtTokenService _tokens;
    private readonly Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> _hasher;

    public AuthController(IdentityUserManager users, JwtTokenService tokens, Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> hasher)
    {
        _users = users;
        _tokens = tokens;
        _hasher = hasher;
    }

    /// <summary>Exchanges a user name and password for an access token. Repeated failures lock the account for a while.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResult>> LoginAsync(LoginInput input)
    {
        var user = await _users.FindByNameAsync(input.UserName);

        // One answer for every failure, so that the response does not reveal which user names exist.
        if (user is null || !user.IsActive || await _users.IsLockedOutAsync(user))
        {
            // Hashing a password costs the same time as checking one, so that the answer does not come faster for a name that does not exist.
            SpendTheTimeOfACheck(input.Password);
            return Rejected();
        }

        if (!await _users.CheckPasswordAsync(user, input.Password))
        {
            await _users.AccessFailedAsync(user);
            return Rejected();
        }

        await _users.ResetAccessFailedCountAsync(user);

        var roles = (await _users.GetRolesAsync(user)).ToList();
        var (token, expires) = _tokens.Create(user, roles);

        return new LoginResult { AccessToken = token, UserId = user.Id, ExpiresAt = expires, UserName = user.UserName ?? string.Empty, Roles = roles };
    }

    private UnauthorizedObjectResult Rejected()
    {
        return Unauthorized(new { error = new { message = "Invalid user name or password." } });
    }

    private void SpendTheTimeOfACheck(string password)
    {
        _hasher.HashPassword(new IdentityUser(Guid.NewGuid(), "nobody", "nobody@example.invalid"), password);
    }
}
