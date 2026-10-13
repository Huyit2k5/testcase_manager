using Volo.Abp.DependencyInjection;
using Volo.Abp.DynamicProxy;

namespace Acme.TestCaseManagement.Projects;

/// <summary>
/// Runs each call to an application service of the module inside the project scope of the caller (see <see cref="ProjectAccessScope"/>),
/// so that the data of a project the caller may not see is not found. A scope that is already set (an outer call, or code that set
/// one on purpose) is kept, so the access is worked out once for a request.
/// </summary>
public class ProjectAccessInterceptor : AbpInterceptor, ITransientDependency
{
    private readonly ProjectAccessProvider _provider;

    public ProjectAccessInterceptor(ProjectAccessProvider provider)
    {
        _provider = provider;
    }

    public override async Task InterceptAsync(IAbpMethodInvocation invocation)
    {
        if (ProjectAccessScope.IsActive)
        {
            await invocation.ProceedAsync();
            return;
        }

        var visible = await _provider.GetVisibleProjectIdsAsync();
        using (visible == null ? ProjectAccessScope.Unrestrict() : ProjectAccessScope.Restrict(visible))
        {
            await invocation.ProceedAsync();
        }
    }
}
