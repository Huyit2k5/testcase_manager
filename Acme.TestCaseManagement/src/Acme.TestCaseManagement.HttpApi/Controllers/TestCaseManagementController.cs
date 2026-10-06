using Acme.TestCaseManagement.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Acme.TestCaseManagement.Controllers;

public abstract class TestCaseManagementController : AbpControllerBase
{
    protected TestCaseManagementController()
    {
        LocalizationResource = typeof(TestCaseManagementResource);
    }
}
