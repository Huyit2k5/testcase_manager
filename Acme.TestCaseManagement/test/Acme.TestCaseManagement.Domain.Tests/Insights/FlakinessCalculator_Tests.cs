using Acme.TestCaseManagement.Enums;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Insights;

/// <summary>Executable form of the flakiness score in plan.md, section 4.10.</summary>
public class FlakinessCalculator_Tests
{
    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0);
    private static readonly Guid TestCaseId = Guid.NewGuid();

    /// <summary>P = Passed, F = Failed, B = Blocked, S = Skipped; one attempt an hour in the order written.</summary>
    private static List<InsightAttempt> Attempts(string pattern, Guid? testCaseId = null) =>
        pattern.Select((c, i) => new InsightAttempt(
            Guid.NewGuid(),
            testCaseId ?? TestCaseId,
            c switch { 'P' => TestResultStatus.Passed, 'F' => TestResultStatus.Failed, 'B' => TestResultStatus.Blocked, _ => TestResultStatus.Skipped },
            1,
            Start.AddHours(i))).ToList();

    private static FlakinessResult Score(string pattern, FlakinessSettings? settings = null) =>
        FlakinessCalculator.Calculate(TestCaseId, Attempts(pattern), settings ?? new FlakinessSettings());

    [Theory]
    // The patterns that Buildkite Test Engine uses as examples, with the score of this module (changes per neighbouring pair).
    [InlineData("PPPPP", 0, 0.00)]
    [InlineData("FFFFF", 0, 0.00)]
    [InlineData("PPFFF", 1, 0.25)]
    [InlineData("PFPFF", 3, 0.75)]
    [InlineData("PFPFP", 4, 1.00)]
    public void The_Score_Is_The_Share_Of_Neighbouring_Outcomes_That_Differ(string pattern, int flips, double score)
    {
        var result = Score(pattern);

        result.Observations.ShouldBe(5);
        result.Flips.ShouldBe(flips);
        result.Score.ShouldBe((decimal)score);
    }

    [Fact]
    public void A_Test_That_Always_Fails_Or_Was_Fixed_Once_Is_Not_Flaky()
    {
        Score("FFFFFFFFFF").Level.ShouldBe(FlakinessLevel.Stable);
        // A regression (passing, then failing for good) and a fix are one change each.
        Score("PPPPPPFFFFFF").Level.ShouldBe(FlakinessLevel.Stable);
        Score("FFFFFFPPPPPP").Level.ShouldBe(FlakinessLevel.Stable);
    }

    [Fact]
    public void Repeated_Changes_Make_A_Test_Flaky_And_A_Few_Make_It_Worth_Watching()
    {
        // 8 changes in 20 outcomes: 8/19 = 0.42.
        var flaky = Score("PFPFPFPFPPPPPPPPPPPP");
        flaky.Flips.ShouldBe(8);
        flaky.Level.ShouldBe(FlakinessLevel.Flaky);

        // 4 changes in 20 outcomes: 4/19 = 0.21.
        var watch = Score("PPPPPPPFPPPPPPPFPPPP");
        watch.Flips.ShouldBe(4);
        watch.Level.ShouldBe(FlakinessLevel.Watch);

        // One failure in a long run of passes is 2 changes: 2/19 = 0.10.
        var oneBlip = Score("PPPPPPPPPPFPPPPPPPPP");
        oneBlip.Flips.ShouldBe(2);
        oneBlip.Level.ShouldBe(FlakinessLevel.Stable);
    }

    [Fact]
    public void The_Levels_Change_At_The_Thresholds_Not_Before()
    {
        var settings = new FlakinessSettings { WindowSize = 11, MinimumObservations = 5, WatchScore = 0.2m, FlakyScore = 0.4m };

        // 10 neighbouring pairs: every change that is not at the edge of the window comes in twos (a lone F is two changes).
        Score("PPPPPPPPPPP", settings).Level.ShouldBe(FlakinessLevel.Stable);
        Score("PFFFFFFFFFF", settings).Score.ShouldBe(0.1m);
        Score("PFFFFFFFFFF", settings).Level.ShouldBe(FlakinessLevel.Stable);
        Score("PPPPPFPPPPP", settings).Score.ShouldBe(0.2m);   // exactly the watch threshold
        Score("PPPPPFPPPPP", settings).Level.ShouldBe(FlakinessLevel.Watch);
        Score("PFPFFFFFFFF", settings).Score.ShouldBe(0.3m);
        Score("PFPFFFFFFFF", settings).Level.ShouldBe(FlakinessLevel.Watch);
        Score("PFPFPPPPPPP", settings).Score.ShouldBe(0.4m);   // exactly the flaky threshold
        Score("PFPFPPPPPPP", settings).Level.ShouldBe(FlakinessLevel.Flaky);
        Score("PPPFPFPFPPP", settings).Score.ShouldBe(0.6m);
    }

    [Fact]
    public void Only_The_Latest_Outcomes_Of_The_Window_Count()
    {
        var settings = new FlakinessSettings { WindowSize = 6 };

        // Fifteen alternating outcomes long ago do not count against a test that has been stable since.
        var result = Score("PFPFPFPFPFPFPFPPPPPPP", settings);

        result.Observations.ShouldBe(6);
        result.Flips.ShouldBe(0);
        result.Level.ShouldBe(FlakinessLevel.Stable);
    }

    [Fact]
    public void Too_Few_Outcomes_Are_Not_Scored()
    {
        var result = Score("PFPF");

        result.Level.ShouldBe(FlakinessLevel.Insufficient);
        result.Observations.ShouldBe(4);
        Score("").Level.ShouldBe(FlakinessLevel.Insufficient);
        Score("").Score.ShouldBe(0m);
        Score("").LastResultAt.ShouldBeNull();
    }

    [Fact]
    public void Blocked_And_Skipped_Are_Not_Outcomes_Of_The_Test()
    {
        var result = Score("PBFSPBFSP");

        result.Observations.ShouldBe(5);
        result.Flips.ShouldBe(4);
        Score("BBBBBBBBBB").Level.ShouldBe(FlakinessLevel.Insufficient);
    }

    [Fact]
    public void The_Outcomes_Are_Taken_In_Time_Order_And_Retries_Count_As_Neighbours()
    {
        var item = Guid.NewGuid();
        var attempts = new List<InsightAttempt>
        {
            // Given out of order: a failed first attempt and a passed retry, then another item that passed.
            new(item, TestCaseId, TestResultStatus.Passed, 2, Start.AddMinutes(1)),
            new(Guid.NewGuid(), TestCaseId, TestResultStatus.Passed, 1, Start.AddHours(1)),
            new(item, TestCaseId, TestResultStatus.Failed, 1, Start),
            new(Guid.NewGuid(), TestCaseId, TestResultStatus.Passed, 1, Start.AddHours(2)),
            new(Guid.NewGuid(), TestCaseId, TestResultStatus.Passed, 1, Start.AddHours(3)),
        };

        var result = FlakinessCalculator.Calculate(TestCaseId, attempts, new FlakinessSettings());

        // F P P P P: one change (the retry that passed).
        result.Flips.ShouldBe(1);
        result.LastFailedAt.ShouldBe(Start);
        result.LastResultAt.ShouldBe(Start.AddHours(3));
    }

    [Fact]
    public void Attempts_At_The_Same_Time_Follow_The_Attempt_Number()
    {
        var item = Guid.NewGuid();
        var attempts = new List<InsightAttempt>
        {
            new(item, TestCaseId, TestResultStatus.Passed, 2, Start),
            new(item, TestCaseId, TestResultStatus.Failed, 1, Start),
        };

        FlakinessCalculator.Calculate(TestCaseId, attempts, new FlakinessSettings()).LastResultAt.ShouldBe(Start);
        FlakinessCalculator.Calculate(TestCaseId, attempts, new FlakinessSettings()).Flips.ShouldBe(1);
    }

    [Fact]
    public void Every_Test_Case_Is_Scored_On_Its_Own_History()
    {
        var steady = Guid.NewGuid();
        var jumpy = Guid.NewGuid();

        var results = FlakinessCalculator.CalculateAll(Attempts("PPPPPP", steady).Concat(Attempts("PFPFPF", jumpy)), new FlakinessSettings());

        results.Count.ShouldBe(2);
        results.Single(r => r.TestCaseId == steady).Level.ShouldBe(FlakinessLevel.Stable);
        results.Single(r => r.TestCaseId == jumpy).Level.ShouldBe(FlakinessLevel.Flaky);
    }

    [Fact]
    public void Settings_Cannot_Make_The_Window_Smaller_Than_Two_Outcomes()
    {
        var result = Score("PFPF", new FlakinessSettings { WindowSize = 0, MinimumObservations = 0 });

        result.Observations.ShouldBe(2);
        result.Flips.ShouldBe(1);
        result.Score.ShouldBe(1m);
    }
}
