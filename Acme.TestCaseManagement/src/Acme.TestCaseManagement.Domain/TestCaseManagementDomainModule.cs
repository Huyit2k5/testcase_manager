using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(TestCaseManagementDomainSharedModule))]
public class TestCaseManagementDomainModule : AbpModule
{
}
