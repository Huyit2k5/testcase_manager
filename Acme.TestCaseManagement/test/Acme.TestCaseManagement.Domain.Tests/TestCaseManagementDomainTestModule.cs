using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(typeof(TestCaseManagementTestBaseModule))]
public class TestCaseManagementDomainTestModule : AbpModule
{
}
