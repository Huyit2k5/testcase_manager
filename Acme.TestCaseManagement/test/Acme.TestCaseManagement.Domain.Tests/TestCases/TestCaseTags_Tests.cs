using Shouldly;
using Volo.Abp;
using Xunit;

namespace Acme.TestCaseManagement.TestCases;

public class TestCaseTags_Tests
{
    private static TestCase NewTestCase() => new(Guid.NewGuid(), null, Guid.NewGuid(), "TC-1", "A test");

    private static string[] Names(TestCase testCase) => testCase.Tags.Select(t => t.Name).Order().ToArray();

    [Theory]
    [InlineData("  smoke ", "smoke")]
    [InlineData("Release   2.1", "Release 2.1")]
    [InlineData("a\tb", "a b")]
    public void A_Tag_Is_Cleaned(string raw, string expected)
    {
        TagNames.Clean(raw).ShouldBe(expected);
    }

    [Fact]
    public void Tags_That_Differ_Only_In_Case_Or_Spacing_Are_One_Tag()
    {
        var testCase = NewTestCase();

        testCase.SetTags(new[] { "Smoke", "smoke", "SMOKE ", "Payments  API", "payments api" });

        Names(testCase).ShouldBe(new[] { "Payments API", "Smoke" });
        testCase.Tags.Select(t => t.NormalizedName).Order().ShouldBe(new[] { "payments api", "smoke" });
    }

    [Fact]
    public void Blank_Tags_Are_Dropped_And_Null_Means_None()
    {
        var testCase = NewTestCase();

        testCase.SetTags(new string?[] { "", "  ", null, "ok" });
        Names(testCase).ShouldBe(new[] { "ok" });

        testCase.SetTags(null);
        testCase.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void A_Tag_That_Stays_Keeps_Its_Row_And_Its_First_Spelling()
    {
        var testCase = NewTestCase();
        testCase.SetTags(new[] { "Smoke", "api" });
        var smoke = testCase.Tags.Single(t => t.NormalizedName == "smoke");

        testCase.SetTags(new[] { "SMOKE", "ui" });

        testCase.Tags.Single(t => t.NormalizedName == "smoke").ShouldBeSameAs(smoke);
        smoke.Name.ShouldBe("Smoke");
        Names(testCase).ShouldBe(new[] { "Smoke", "ui" });
    }

    [Theory]
    [InlineData("a,b")]
    [InlineData("a;b")]
    [InlineData("bad\u0001tag")]
    public void A_Tag_With_A_Separator_Or_A_Control_Character_Is_Refused_And_Nothing_Changes(string bad)
    {
        var testCase = NewTestCase();
        testCase.SetTags(new[] { "keep" });

        var exception = Should.Throw<BusinessException>(() => testCase.SetTags(new[] { "fine", bad }));

        exception.Code.ShouldBe(TestCaseManagementErrorCodes.InvalidTag);
        Names(testCase).ShouldBe(new[] { "keep" });
    }

    [Fact]
    public void A_Tag_Has_At_Most_Fifty_Characters()
    {
        var testCase = NewTestCase();

        testCase.SetTags(new[] { new string('a', 50) });
        Should.Throw<BusinessException>(() => testCase.SetTags(new[] { new string('a', 51) })).Code.ShouldBe(TestCaseManagementErrorCodes.InvalidTag);
    }

    [Fact]
    public void A_Test_Case_Has_At_Most_Twenty_Tags()
    {
        var testCase = NewTestCase();
        var twenty = Enumerable.Range(1, 20).Select(i => $"tag{i}").ToList();

        testCase.SetTags(twenty);
        testCase.Tags.Count.ShouldBe(20);

        var exception = Should.Throw<BusinessException>(() => testCase.SetTags(twenty.Append("tag21")));
        exception.Code.ShouldBe(TestCaseManagementErrorCodes.TooManyTags);
        exception.Data["Count"].ShouldBe(21);
        testCase.Tags.Count.ShouldBe(20);

        // Doubles do not count against the limit.
        testCase.SetTags(twenty.Concat(twenty.Select(t => t.ToUpperInvariant())));
        testCase.Tags.Count.ShouldBe(20);
    }

    [Fact]
    public void The_Cell_Of_A_File_Is_Split_On_Semicolons()
    {
        TagNames.Split("smoke; payments ;;  ui").ShouldBe(new[] { "smoke", "payments", "ui" });
        TagNames.Split(null).ShouldBeEmpty();
        TagNames.Split("").ShouldBeEmpty();
    }
}
