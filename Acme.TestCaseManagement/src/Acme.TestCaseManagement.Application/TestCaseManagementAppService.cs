using Acme.TestCaseManagement.Localization;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement;

public abstract class TestCaseManagementAppService : ApplicationService
{
    protected TestCaseManagementAppService()
    {
        LocalizationResource = typeof(TestCaseManagementResource);
        ObjectMapperContext = typeof(TestCaseManagementApplicationModule);
    }
}
