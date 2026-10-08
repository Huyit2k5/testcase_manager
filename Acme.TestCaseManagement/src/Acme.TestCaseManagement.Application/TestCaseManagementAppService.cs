using Acme.TestCaseManagement.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.DistributedLocking;

namespace Acme.TestCaseManagement;

public abstract class TestCaseManagementAppService : ApplicationService
{
    protected TestCaseManagementAppService()
    {
        LocalizationResource = typeof(TestCaseManagementResource);
        ObjectMapperContext = typeof(TestCaseManagementApplicationModule);
    }

    /// <summary>
    /// Makes this request the only one that changes <paramref name="what"/> until its unit of work is over (committed or rolled back),
    /// so that two requests cannot both read the old state and each add to it. Take it before reading what is to be changed. A request
    /// that cannot get it in time is refused with <see cref="TestCaseManagementErrorCodes.OperationInProgress"/>.
    /// </summary>
    protected virtual async Task LockUntilTheRequestEndsAsync(string what)
    {
        var wait = LazyServiceProvider.LazyGetRequiredService<IOptions<TestCaseManagementLockOptions>>().Value.Wait;
        var handle = await LazyServiceProvider.LazyGetRequiredService<IAbpDistributedLock>()
            .TryAcquireAsync($"tcm:{CurrentTenant.Id}:{what}", wait);
        if (handle == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.OperationInProgress);
        }

        var unitOfWork = CurrentUnitOfWork;
        if (unitOfWork == null)
        {
            // Without a unit of work there is nothing to hold it for.
            await handle.DisposeAsync();
            return;
        }

        unitOfWork.Disposed += (_, _) => handle.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
