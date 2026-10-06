using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(
    typeof(AbpDddApplicationModule),
    typeof(AbpAutoMapperModule),
    typeof(TestCaseManagementDomainModule),
    typeof(TestCaseManagementApplicationContractsModule))]
public class TestCaseManagementApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAutoMapperObjectMapper<TestCaseManagementApplicationModule>();

        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<TestCaseManagementApplicationModule>(validate: true);
        });
    }
}
