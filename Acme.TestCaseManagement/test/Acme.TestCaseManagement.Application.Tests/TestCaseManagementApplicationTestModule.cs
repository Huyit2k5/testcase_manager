using Acme.TestCaseManagement.StepSuggestions;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(typeof(TestCaseManagementTestBaseModule))]
public class TestCaseManagementApplicationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // The step suggestion tests control what the model "says"; the built-in provider has its own tests.
        // A change that finds the lock taken gives up quickly in tests instead of waiting 30 seconds.
        Configure<TestCaseManagementLockOptions>(options => options.Wait = TimeSpan.FromMilliseconds(200));

        context.Services.AddSingleton<FakeStepSuggestionProvider>();
        context.Services.AddSingleton<IStepSuggestionProvider>(sp => sp.GetRequiredService<FakeStepSuggestionProvider>());
    }
}
