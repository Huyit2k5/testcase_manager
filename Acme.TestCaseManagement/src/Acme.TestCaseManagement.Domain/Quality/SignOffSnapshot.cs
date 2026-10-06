using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acme.TestCaseManagement.Quality;

/// <summary>
/// The content frozen into a sign-off report: the complete gate evaluation (scope, thresholds, metrics, criteria
/// results, open defects) at the moment sign-off started. Stored as JSON together with its SHA-256.
/// </summary>
public record SignOffSnapshot(int SchemaVersion, DateTime GeneratedTime, QualityGateEvaluation Evaluation)
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Names instead of numbers, so a stored snapshot stays readable and survives a reordering of enum members.
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(SignOffSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static SignOffSnapshot Deserialize(string json)
    {
        return JsonSerializer.Deserialize<SignOffSnapshot>(json, JsonOptions)
               ?? throw new InvalidOperationException("The sign-off snapshot is empty.");
    }

    /// <summary>Lowercase hexadecimal SHA-256 of the exact stored JSON text.</summary>
    public static string ComputeHash(string json)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
