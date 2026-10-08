namespace Acme.TestCaseManagement.StepSuggestions;

/// <summary>A provider the tests control: what it says, whether it is on, and what it was asked.</summary>
public class FakeStepSuggestionProvider : IStepSuggestionProvider
{
    public bool Enabled { get; set; } = true;

    public List<SuggestedStepDto> Answer { get; set; } = new();

    public Exception? Failure { get; set; }

    public StepSuggestionRequest? LastRequest { get; private set; }

    public int Calls { get; private set; }

    public bool IsEnabled => Enabled;

    public void Reset()
    {
        Enabled = true;
        Answer = new List<SuggestedStepDto> { new() { Action = "Open the page", ExpectedResult = "It opens" } };
        Failure = null;
        LastRequest = null;
        Calls = 0;
    }

    public Task<IReadOnlyList<SuggestedStepDto>> SuggestAsync(StepSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastRequest = request;
        if (Failure != null)
        {
            throw Failure;
        }

        return Task.FromResult<IReadOnlyList<SuggestedStepDto>>(Answer);
    }
}
