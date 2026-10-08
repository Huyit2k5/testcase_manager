using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.StepSuggestions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Authorization.Permissions;
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

        // An API key is a principal that is not a user: this provider is what gives it its one permission.
        Configure<AbpPermissionOptions>(options =>
        {
            options.ValueProviders.Add<ApiKeyPermissionValueProvider>();
        });

        // Step suggestions (FR-027): the built-in provider reads the host's configuration section TestCaseManagement:AiSuggestions.
        // Without an endpoint there, suggestions are off. A redirect is not followed: a key must not travel to another address.
        Configure<TestCaseManagementAiOptions>(context.Services.GetConfiguration().GetSection(TestCaseManagementAiOptions.Section));
        context.Services
            .AddHttpClient(OpenAiCompatibleStepSuggestionProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<TestCaseManagementApplicationModule>(validate: true);
        });
    }
}
