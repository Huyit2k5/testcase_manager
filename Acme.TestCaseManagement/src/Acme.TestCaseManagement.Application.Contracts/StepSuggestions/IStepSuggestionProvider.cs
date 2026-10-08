namespace Acme.TestCaseManagement.StepSuggestions;

/// <summary>What a provider is asked: the text is already trimmed and within <see cref="StepSuggestionLimits"/>.</summary>
public sealed record StepSuggestionRequest(string RequirementText, string? Title, int MaxSteps, string Language);

/// <summary>
/// The extension point of FR-027: whatever turns a requirement text into proposed steps. The module ships one that calls an
/// OpenAI-compatible endpoint (OpenAI, or a model served by Ollama, vLLM, LM Studio...), configured with
/// <c>TestCaseManagementAiOptions</c>. A host that uses another service registers its own implementation in place of it
/// (<c>[Dependency(ReplaceServices = true)]</c> with <c>[ExposeServices(typeof(IStepSuggestionProvider))]</c>).
/// </summary>
/// <remarks>
/// The application service treats the answer of a provider as untrusted text: it drops empty steps and caps lengths and counts
/// whatever the provider returned. A provider should throw <c>BusinessException</c> with
/// <c>TestCaseManagementErrorCodes.StepSuggestionFailed</c> when its service cannot be reached, and must never put a key or a
/// secret into an exception message or a log entry.
/// </remarks>
public interface IStepSuggestionProvider
{
    /// <summary>False when the provider is not set up (no endpoint, no key...): suggestions are then off.</summary>
    bool IsEnabled { get; }

    Task<IReadOnlyList<SuggestedStepDto>> SuggestAsync(StepSuggestionRequest request, CancellationToken cancellationToken = default);
}
