using System.Text.Json;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>Serialized form of a step inside <see cref="TestCaseVersion.StepsJson"/>.</summary>
public record TestStepSnapshot(Guid Id, int Order, string Action, string ExpectedResult, string? TestData)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<TestStepSnapshot> steps)
    {
        return JsonSerializer.Serialize(steps.OrderBy(s => s.Order).ToList(), JsonOptions);
    }

    public static List<TestStepSnapshot> Deserialize(string stepsJson)
    {
        return JsonSerializer.Deserialize<List<TestStepSnapshot>>(stepsJson, JsonOptions) ?? new List<TestStepSnapshot>();
    }
}
