using Volo.Abp.BlobStoring;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(AbpBlobStoringModule),
    typeof(TestCaseManagementDomainSharedModule))]
public class TestCaseManagementDomainModule : AbpModule
{
}
