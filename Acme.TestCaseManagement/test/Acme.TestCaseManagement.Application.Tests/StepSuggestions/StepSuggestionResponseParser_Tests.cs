using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.StepSuggestions;

/// <summary>What is read out of the text of a model, and how steps of any provider are cleaned.</summary>
public class StepSuggestionResponseParser_Tests
{
    private static SuggestedStepDto Step(string action, string expected, string? data = null) =>
        new() { Action = action, ExpectedResult = expected, TestData = data };

    [Fact]
    public void A_json_object_with_a_steps_list_is_read()
    {
        var steps = StepSuggestionResponseParser.Parse(
            """{"steps":[{"action":"Open the login page","expectedResult":"The form is shown","testData":""},{"action":"Sign in","expectedResult":"The dashboard opens","testData":"user=ann"}]}""");

        steps.Count.ShouldBe(2);
        steps[0].Action.ShouldBe("Open the login page");
        steps[1].TestData.ShouldBe("user=ann");
    }

    [Fact]
    public void A_code_fence_and_talk_around_the_json_are_ignored()
    {
        var steps = StepSuggestionResponseParser.Parse(
            "Sure! Here are the steps:\n```json\n{\"steps\":[{\"action\":\"A\",\"expectedResult\":\"B\"}]}\n```\nLet me know if you need more.");

        steps.Count.ShouldBe(1);
        steps[0].Action.ShouldBe("A");
    }

    [Fact]
    public void A_bare_list_and_other_spellings_of_the_fields_are_accepted()
    {
        var steps = StepSuggestionResponseParser.Parse(
            """[{"step":"Click Pay","expected_result":"Paid","test_data":"4111"},{"Action":"Check the receipt","Expected":"A receipt is shown"}]""");

        steps.Select(s => (s.Action, s.ExpectedResult, s.TestData)).ShouldBe(new[]
        {
            ("Click Pay", "Paid", "4111"),
            ("Check the receipt", "A receipt is shown", ""),
        });
    }

    [Fact]
    public void Brackets_inside_strings_do_not_end_the_json_early()
    {
        var steps = StepSuggestionResponseParser.Parse("""{"steps":[{"action":"Type } and ] and \" in the box","expectedResult":"Shown {as is}"}]} trailing } text""");

        steps.Count.ShouldBe(1);
        steps[0].Action.ShouldBe("Type } and ] and \" in the box");
        steps[0].ExpectedResult.ShouldBe("Shown {as is}");
    }

    [Fact]
    public void A_bracket_in_the_talk_before_the_json_does_not_hide_the_json()
    {
        var steps = StepSuggestionResponseParser.Parse(
            "Here are the steps [see below] (numbered): {\"steps\":[{\"action\":\"A\",\"expectedResult\":\"B\"}]}");

        steps.Single().Action.ShouldBe("A");
    }

    [Fact]
    public void The_first_region_that_gives_steps_wins_and_regions_without_steps_are_skipped()
    {
        var steps = StepSuggestionResponseParser.Parse(
            "[1, 2] {\"note\":\"x\"} [{\"action\":\"First\",\"expectedResult\":\"One\"}] [{\"action\":\"Second\",\"expectedResult\":\"Two\"}]");

        steps.Select(s => s.Action).ShouldBe(new[] { "First" });
    }

    [Fact]
    public void A_text_full_of_brackets_is_tried_only_a_limited_number_of_times()
    {
        var text = string.Concat(Enumerable.Repeat("[x]", 5000)) + "{\"steps\":[{\"action\":\"Late\",\"expectedResult\":\"Too late\"}]}";

        StepSuggestionResponseParser.Parse(text).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I cannot help with that.")]
    [InlineData("{\"steps\": [ {\"action\": \"unfinished")]
    [InlineData("{\"answer\": 42}")]
    [InlineData("[1, 2, 3]")]
    public void Text_without_steps_gives_an_empty_list(string? text)
    {
        StepSuggestionResponseParser.Parse(text).ShouldBeEmpty();
    }

    [Fact]
    public void Sanitize_drops_empty_and_repeated_steps_and_keeps_the_order()
    {
        var steps = StepSuggestionResponseParser.Sanitize(new[]
        {
            Step("Open", "Shown"),
            Step("  ", "Nothing to do"),
            Step("No expectation", ""),
            Step("OPEN", "shown"),
            Step("Close", "Closed", "  "),
        }, maxSteps: 10);

        steps.Select(s => s.Action).ShouldBe(new[] { "Open", "Close" });
        steps[1].TestData.ShouldBeNull();
    }

    [Fact]
    public void Sanitize_caps_the_number_of_steps_and_the_length_of_every_field()
    {
        var long4000 = new string('x', 5000);
        var steps = StepSuggestionResponseParser.Sanitize(
            Enumerable.Range(1, 30).Select(i => Step($"Step {i}", long4000, long4000)), maxSteps: 5);

        steps.Count.ShouldBe(5);
        steps[0].ExpectedResult.Length.ShouldBe(TestStepConsts.MaxTextLength);
        steps[0].TestData!.Length.ShouldBe(1000);
    }

    [Fact]
    public void Sanitize_removes_control_and_invisible_characters_but_keeps_line_breaks()
    {
        var steps = StepSuggestionResponseParser.Sanitize(new[] { Step("Open\u0000 the​ page\nthen wait", "Sh\u0007own﻿") }, 5);

        steps.Single().Action.ShouldBe("Open the page\nthen wait");
        steps.Single().ExpectedResult.ShouldBe("Shown");
    }

    [Fact]
    public void Sanitize_never_cuts_an_emoji_in_half_and_replaces_an_unpaired_surrogate()
    {
        var atTheLimit = new string('x', TestStepConsts.MaxTextLength - 1) + "\U0001F600 tail";
        var steps = StepSuggestionResponseParser.Sanitize(new[]
        {
            Step(atTheLimit, "ok"),
            Step("Lone \ud83d surrogate", "ok"),
        }, 5);

        steps[0].Action.Length.ShouldBe(TestStepConsts.MaxTextLength - 1);
        foreach (var step in steps)
        {
            step.Action.EnumerateRunes().ShouldAllBe(r => r != System.Text.Rune.ReplacementChar);
        }

        steps[1].Action.ShouldBe("Lone  surrogate");
    }

    [Fact]
    public void Sanitize_keeps_an_emoji_that_fits()
    {
        StepSuggestionResponseParser.Sanitize(new[] { Step("Click \U0001F600", "Smile \U0001F44D") }, 5).Single().Action.ShouldBe("Click \U0001F600");
    }

    [Fact]
    public void Sanitize_of_nothing_is_empty()
    {
        StepSuggestionResponseParser.Sanitize(null, 5).ShouldBeEmpty();
    }
}
