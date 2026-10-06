using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule),
    typeof(TestCaseManagementDomainSharedModule))]
public class TestCaseManagementApplicationContractsModule : AbpModule
{
}
