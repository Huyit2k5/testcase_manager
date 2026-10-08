using System.Text;
using System.Text.Json;

namespace Acme.TestCaseManagement.StepSuggestions;

/// <summary>
/// Turns what a model wrote into steps, and cleans steps from any provider. The text of a model is untrusted data: it may be wrapped
/// in a code fence, carry a sentence before the JSON, use another spelling of a field, repeat itself, or be far too long.
/// </summary>
internal static class StepSuggestionResponseParser
{
    private const int MaxFieldLength = TestStepConsts.MaxTextLength;
    private const int MaxTestDataLength = 1000;

    /// <summary>The most places in a text that are tried as the start of the JSON.</summary>
    private const int MaxCandidates = 50;

    /// <summary>The most opening brackets that are allowed not to close before the search is given up.</summary>
    private const int MaxUnclosed = 20;

    /// <summary>
    /// Reads the steps out of the text of a model's answer: a JSON object with a <c>steps</c> list, or a bare list. Every opening
    /// bracket is tried in turn until one gives steps, so a bracket in the talk before the JSON ("[see below]") does not hide it.
    /// Anything that is not recognised gives an empty list; the caller decides what that means.
    /// </summary>
    public static List<SuggestedStepDto> Parse(string? modelText)
    {
        foreach (var json in Candidates(modelText))
        {
            var steps = TryRead(json);
            if (steps.Count > 0)
            {
                return steps;
            }
        }

        return new List<SuggestedStepDto>();
    }

    private static List<SuggestedStepDto> TryRead(string json)
    {
        var result = new List<SuggestedStepDto>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = true });
            var list = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement,
                JsonValueKind.Object when TryGetProperty(document.RootElement, out var steps, "steps", "testSteps", "test_steps")
                    && steps.ValueKind == JsonValueKind.Array => steps,
                _ => default,
            };

            if (list.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result.Add(new SuggestedStepDto
                {
                    Action = ReadText(item, "action", "step", "description"),
                    ExpectedResult = ReadText(item, "expectedResult", "expected_result", "expected", "result"),
                    TestData = ReadText(item, "testData", "test_data", "data"),
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return new List<SuggestedStepDto>();
        }
    }

    /// <summary>
    /// Drops steps without an action or an expected result and repeated steps, removes control characters, caps every length and the
    /// number of steps. Applied to the answer of every provider, the built-in one or a host's.
    /// </summary>
    public static List<SuggestedStepDto> Sanitize(IEnumerable<SuggestedStepDto>? steps, int maxSteps)
    {
        var result = new List<SuggestedStepDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps ?? Array.Empty<SuggestedStepDto>())
        {
            if (result.Count >= maxSteps)
            {
                break;
            }

            var action = Clean(step.Action, MaxFieldLength);
            var expected = Clean(step.ExpectedResult, MaxFieldLength);
            if (action.Length == 0 || expected.Length == 0 || !seen.Add(action + "\u0001" + expected))
            {
                continue;
            }

            var data = Clean(step.TestData, MaxTestDataLength);
            result.Add(new SuggestedStepDto { Action = action, ExpectedResult = expected, TestData = data.Length == 0 ? null : data });
        }

        return result;
    }

    /// <summary>
    /// The balanced bracket regions of the text, one for each opening bracket that is not inside an earlier region's string, in order of
    /// appearance. A code fence and talk around the JSON are ignored simply because they are not regions.
    /// </summary>
    internal static IEnumerable<string> Candidates(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var tried = 0;
        var unclosed = 0;
        for (var start = text.IndexOfAny(new[] { '{', '[' }); start >= 0 && tried < MaxCandidates; start = text.IndexOfAny(new[] { '{', '[' }, start + 1))
        {
            var end = MatchingEnd(text, start);
            if (end < 0)
            {
                // Finding that a bracket never closes reads to the end of the text; a text of nothing but opening brackets would do it
                // for every one of them. A few are prose ("[see below"), a lot is not an answer.
                if (++unclosed >= MaxUnclosed)
                {
                    yield break;
                }

                continue;
            }

            tried++;
            yield return text.Substring(start, end - start + 1);
        }
    }

    /// <summary>The index of the bracket that closes the one at <paramref name="start"/>, skipping brackets inside strings; -1 when it is not closed.</summary>
    private static int MatchingEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                    depth++;
                    break;
                case '}':
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string ReadText(JsonElement item, params string[] names)
    {
        if (!TryGetProperty(item, out var value, names))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };
    }

    private static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, maxLength));

        // By character (code point), not by UTF-16 unit: a cut can never split an emoji, and an unpaired surrogate is replaced.
        foreach (var rune in text.EnumerateRunes())
        {
            if (builder.Length + rune.Utf16SequenceLength > maxLength)
            {
                break;
            }

            // Newlines and tabs stay; other control characters (and the zero-width ones used to hide text) go.
            if (rune.Value == '\n' || rune.Value == '\t'
                || (!Rune.IsControl(rune) && rune.Value != 0x200B && rune.Value != 0x200E && rune.Value != 0x200F && rune.Value != 0xFEFF && rune != Rune.ReplacementChar))
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString().Trim();
    }
}
