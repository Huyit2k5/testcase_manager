using Acme.TestCaseManagement.EntityFrameworkCore;
using Acme.TestCaseManagement.EntityFrameworkCore.Repositories;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.TestCases;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(
    typeof(AbpEntityFrameworkCoreModule),
    typeof(TestCaseManagementDomainModule))]
public class TestCaseManagementEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<TestCaseManagementDbContext>(options =>
        {
            // Repositories may ask for either the interface or the class; both must resolve to the same
            // DbContext instance inside a unit of work, otherwise changes are saved by different contexts.
            options.ReplaceDbContext<ITestCaseManagementDbContext>();
            options.AddDefaultRepositories(includeAllEntities: true);
            options.AddRepository<TestCase, EfCoreTestCaseRepository>();
            options.AddRepository<DefectLink, EfCoreDefectLinkRepository>();

            options.Entity<TestCase>(o => o.DefaultWithDetailsFunc = q => q.IncludeDetails());
            options.Entity<TestRun>(o => o.DefaultWithDetailsFunc = q => q.IncludeDetails());
            options.Entity<SignOffReport>(o => o.DefaultWithDetailsFunc = q => q.IncludeDetails());
        });
    }
}
