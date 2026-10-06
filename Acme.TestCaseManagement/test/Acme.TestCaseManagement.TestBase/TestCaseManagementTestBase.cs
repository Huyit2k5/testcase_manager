using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Testing;
using Volo.Abp.Uow;

namespace Acme.TestCaseManagement;

public abstract class TestCaseManagementTestBase<TStartupModule> : AbpIntegratedTest<TStartupModule>
    where TStartupModule : IAbpModule
{
    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }

    /// <summary>
    /// Runs the code that follows (until the returned scope is disposed) as the given authenticated user,
    /// as <c>ICurrentUser</c> sees it. Used for sign-off, which records who approved.
    /// </summary>
    protected virtual IDisposable ChangeUser(Guid userId, string userName = "tester")
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(AbpClaimTypes.UserId, userId.ToString()),
                new Claim(AbpClaimTypes.UserName, userName),
            },
            authenticationType: "Test");

        return GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(identity));
    }

    /// <summary>Flushes pending changes of the ambient unit of work so that subsequent queries see them.</summary>
    protected virtual async Task SaveChangesAsync()
    {
        await GetRequiredService<IUnitOfWorkManager>().Current!.SaveChangesAsync();
    }

    protected virtual async Task WithUnitOfWorkAsync(Func<Task> action)
    {
        using var scope = ServiceProvider.CreateScope();
        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        await action();
        await uow.CompleteAsync();
    }

    protected virtual async Task<TResult> WithUnitOfWorkAsync<TResult>(Func<Task<TResult>> func)
    {
        using var scope = ServiceProvider.CreateScope();
        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var result = await func();
        await uow.CompleteAsync();
        return result;
    }
}
