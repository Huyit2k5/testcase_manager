using Acme.TestCaseManagement.Validation;
using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;
using Volo.Abp.Validation;

namespace Acme.TestCaseManagement;

[DependsOn(
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule),
    typeof(TestCaseManagementDomainSharedModule))]
public class TestCaseManagementApplicationContractsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpValidationOptions>(options =>
        {
            options.ObjectValidationContributors.Add<EnumRangeValidationContributor>();
        });
    }
}
