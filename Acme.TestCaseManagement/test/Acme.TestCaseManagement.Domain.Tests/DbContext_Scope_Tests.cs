using Acme.TestCaseManagement.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace Acme.TestCaseManagement;

public class DbContext_Scope_Tests : TestCaseManagementDomainTestBase
{
    [Fact]
    public async Task All_Repositories_Should_Share_One_DbContext_Per_Unit_Of_Work()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var byInterface = await GetRequiredService<IDbContextProvider<ITestCaseManagementDbContext>>().GetDbContextAsync();
            var byClass = await GetRequiredService<IDbContextProvider<TestCaseManagementDbContext>>().GetDbContextAsync();

            ReferenceEquals(byInterface, byClass).ShouldBeTrue();
        });
    }
}
