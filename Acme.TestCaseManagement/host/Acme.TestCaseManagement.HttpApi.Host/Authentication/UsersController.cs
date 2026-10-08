using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Identity;

namespace Acme.TestCaseManagement.Authentication;

public class HostUserDto
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// The people of the sample host, for assigning tests to. A host with its own users provides its own list (in ABP applications the
/// Angular adapter reads ABP's user lookup instead); this is the same thing for the sample host, which has no Identity HTTP API.
/// </summary>
[RemoteService(Name = "HostUsers")]
[Route("api/host/users")]
[Authorize]
public class UsersController : AbpControllerBase
{
    private const int MaxUsers = 100;

    private readonly IIdentityUserRepository _users;

    public UsersController(IIdentityUserRepository users)
    {
        _users = users;
    }

    /// <summary>The active users, by name; at most 100.</summary>
    [HttpGet]
    public virtual async Task<List<HostUserDto>> GetListAsync()
    {
        var users = await _users.GetListAsync("UserName", MaxUsers, 0, includeDetails: false);

        return users
            .Where(u => u.IsActive)
            .Select(u => new HostUserDto
            {
                Id = u.Id,
                UserName = u.UserName,
                DisplayName = string.Join(' ', new[] { u.Name, u.Surname }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } full ? full : u.UserName,
            })
            .ToList();
    }
}
