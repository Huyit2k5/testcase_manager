using Acme.TestCaseManagement.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Acme.TestCaseManagement.Projects;

/// <summary>
/// Works out which projects the caller may see: all of them for someone who manages projects (and for a caller that is not a user, such
/// as an API key); otherwise the projects that are not restricted, the ones the caller is a member of, and what has no project yet.
/// </summary>
public class ProjectAccessProvider : ITransientDependency
{
    private readonly IRepository<Project, Guid> _projects;
    private readonly IRepository<ProjectMember, Guid> _members;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;

    public ProjectAccessProvider(
        IRepository<Project, Guid> projects,
        IRepository<ProjectMember, Guid> members,
        ICurrentUser currentUser,
        IPermissionChecker permissions)
    {
        _projects = projects;
        _members = members;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    /// <summary>The projects in reach, or null when every project is.</summary>
    public virtual async Task<IReadOnlyCollection<Guid>?> GetVisibleProjectIdsAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.Id == null)
        {
            return null;
        }

        if (await _permissions.IsGrantedAsync(TestCaseManagementPermissions.Projects.Manage))
        {
            return null;
        }

        // Read with every project in reach: this is what decides what the caller will be able to see.
        using (ProjectAccessScope.Unrestrict())
        {
            var userId = _currentUser.Id.Value;
            var open = (await _projects.GetListAsync(p => !p.IsRestricted)).Select(p => p.Id);
            var member = (await _members.GetListAsync(m => m.UserId == userId)).Select(m => m.ProjectId);
            return open.Concat(member).Distinct().ToList();
        }
    }
}
