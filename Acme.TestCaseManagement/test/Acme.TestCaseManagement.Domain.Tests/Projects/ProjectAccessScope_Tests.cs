using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Projects;

public class ProjectAccessScope_Tests
{
    [Fact]
    public void With_No_Scope_Every_Project_Is_In_Reach()
    {
        ProjectAccessScope.IsActive.ShouldBeFalse();
        ProjectAccessScope.Unrestricted.ShouldBeTrue();
    }

    [Fact]
    public void A_Restricted_Scope_Holds_Its_Projects_And_What_Has_No_Project_Yet_And_Ends_When_It_Is_Disposed()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();

        using (ProjectAccessScope.Restrict(new[] { one, two, one }))
        {
            ProjectAccessScope.IsActive.ShouldBeTrue();
            ProjectAccessScope.Unrestricted.ShouldBeFalse();
            ProjectAccessScope.VisibleProjectIds.ShouldBe(new[] { one, two, Guid.Empty }, ignoreOrder: true);
        }

        ProjectAccessScope.IsActive.ShouldBeFalse();
        ProjectAccessScope.Unrestricted.ShouldBeTrue();
    }

    [Fact]
    public void A_Scope_Inside_Another_Gives_Way_To_It_Again_When_It_Ends()
    {
        var one = Guid.NewGuid();

        using (ProjectAccessScope.Restrict(new[] { one }))
        {
            using (ProjectAccessScope.Unrestrict())
            {
                ProjectAccessScope.Unrestricted.ShouldBeTrue();   // while the access of a person is worked out
            }

            ProjectAccessScope.Unrestricted.ShouldBeFalse();
            ProjectAccessScope.VisibleProjectIds.ShouldContain(one);
        }
    }

    [Fact]
    public async Task The_Scope_Follows_The_Flow_Of_The_Call_Into_The_Tasks_It_Starts()
    {
        var one = Guid.NewGuid();
        using var scope = ProjectAccessScope.Restrict(new[] { one });

        var inside = await Task.Run(() => ProjectAccessScope.Unrestricted);
        var later = await Task.Run(async () =>
        {
            await Task.Yield();
            return ProjectAccessScope.VisibleProjectIds.Contains(one);
        });

        inside.ShouldBeFalse();
        later.ShouldBeTrue();
    }
}
