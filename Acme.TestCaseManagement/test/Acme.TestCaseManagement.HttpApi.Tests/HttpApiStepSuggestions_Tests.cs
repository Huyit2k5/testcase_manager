using System.Net;
using System.Text;
using Acme.TestCaseManagement.StepSuggestions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>A model that answers from a script: what it is asked is recorded, and what it says is chosen by the test.</summary>
public sealed class FakeModelHandler : HttpMessageHandler
{
    public const string Secret = "sk-http-test-secret";

    public volatile string Mode = "ok";

    public string? LastBody { get; set; }

    public string? LastAuthorization { get; private set; }

    public Uri? LastUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        if (Mode == "down")
        {
            throw new HttpRequestException("connection refused");
        }

        if (Mode == "error")
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom " + Secret) };
        }

        var content = Mode == "empty"
            ? "Sorry, I cannot help."
            : """{"steps":[{"action":"Open the payment page","expectedResult":"The card form is shown","testData":""},{"action":"Pay with 4111 1111 1111 1111","expectedResult":"The payment is accepted","testData":"4111 1111 1111 1111"}]}""";
        var answer = System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
    }
}

/// <summary>The sample host with a model configured the way a real host does it: the configuration section, and nothing else.</summary>
public class AiEnabledHost : TestCaseManagementHost
{
    public FakeModelHandler Model { get; } = new();

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        builder.UseSetting("TestCaseManagement:AiSuggestions:Endpoint", "http://fake-model.test/v1/chat/completions?key=in-url");
        builder.UseSetting("TestCaseManagement:AiSuggestions:ApiKey", FakeModelHandler.Secret);
        builder.UseSetting("TestCaseManagement:AiSuggestions:Model", "fake-model");
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(OpenAiCompatibleStepSuggestionProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Model));
    }
}

/// <summary>Step suggestions over HTTP, with a configured model and without one.</summary>
public class HttpApiStepSuggestions_Tests : IClassFixture<AiEnabledHost>, IClassFixture<TestCaseManagementHost>
{
    private const string Root = "/api/test-case-management/step-suggestions";
    private const string Requirement = "As a customer I can pay my order with a card and get a receipt by e-mail.";

    private readonly AiEnabledHost _enabled;
    private readonly TestCaseManagementHost _plain;

    public HttpApiStepSuggestions_Tests(AiEnabledHost enabled, TestCaseManagementHost plain)
    {
        _enabled = enabled;
        _plain = plain;
        _enabled.Model.Mode = "ok";
    }

    [Fact]
    public async Task A_host_without_a_model_says_so_and_refuses_to_suggest()
    {
        var tester = await ApiClient.LoginAsync(_plain, "tester");

        (await tester.GetAsync<StepSuggestionStatusDto>($"{Root}/status")).Enabled.ShouldBeFalse();

        var (status, error) = await tester.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = Requirement });
        status.ShouldBe(HttpStatusCode.Forbidden);
        ((string?)error["code"]).ShouldBe(TestCaseManagementErrorCodes.StepSuggestionNotConfigured);
    }

    [Fact]
    public async Task A_configured_model_is_reached_with_the_key_and_its_steps_come_back()
    {
        var tester = await ApiClient.LoginAsync(_enabled, "tester");

        (await tester.GetAsync<StepSuggestionStatusDto>($"{Root}/status")).Enabled.ShouldBeTrue();
        var result = await tester.PostAsync<StepSuggestionResultDto>(Root, new SuggestStepsInput { RequirementText = Requirement, Title = "Pay by card", MaxSteps = 5, Language = "vi" });

        result.Steps.Count.ShouldBe(2);
        result.Steps[1].TestData.ShouldBe("4111 1111 1111 1111");

        _enabled.Model.LastAuthorization.ShouldBe("Bearer " + FakeModelHandler.Secret);
        _enabled.Model.LastUri!.Host.ShouldBe("fake-model.test");
        _enabled.Model.LastBody!.ShouldContain("fake-model");
        _enabled.Model.LastBody!.ShouldContain(Requirement);
        _enabled.Model.LastBody!.ShouldContain("at most 5 steps");
        _enabled.Model.LastBody!.ShouldContain("Vietnamese");
        _enabled.Model.LastBody!.ShouldNotContain(FakeModelHandler.Secret);
    }

    [Fact]
    public async Task The_key_never_appears_in_an_answer_of_the_api()
    {
        var qaLead = await ApiClient.LoginAsync(_enabled, "qa.lead");

        foreach (var path in new[] { $"{Root}/status" })
        {
            using var response = await qaLead.SendRawAsync(HttpMethod.Get, path);
            (await response.Content.ReadAsStringAsync()).ShouldNotContain(FakeModelHandler.Secret);
        }

        _enabled.Model.Mode = "error";
        using var failed = await qaLead.SendRawAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = Requirement });
        var text = await failed.Content.ReadAsStringAsync();
        failed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        text.ShouldNotContain(FakeModelHandler.Secret);
        text.ShouldNotContain("in-url");
        text.ShouldNotContain("boom");
        text.ShouldContain(TestCaseManagementErrorCodes.StepSuggestionFailed);
    }

    [Theory]
    [InlineData("down", TestCaseManagementErrorCodes.StepSuggestionFailed)]
    [InlineData("error", TestCaseManagementErrorCodes.StepSuggestionFailed)]
    [InlineData("empty", TestCaseManagementErrorCodes.StepSuggestionNoUsableSteps)]
    public async Task A_model_that_is_down_refuses_or_says_nothing_useful_is_a_clear_error(string mode, string expectedCode)
    {
        var tester = await ApiClient.LoginAsync(_enabled, "tester");
        _enabled.Model.Mode = mode;

        var (status, error) = await tester.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = Requirement });

        status.ShouldBe(HttpStatusCode.Forbidden);
        ((string?)error["code"]).ShouldBe(expectedCode);
        ((string?)error["message"]).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_error_is_in_the_language_of_the_caller()
    {
        var tester = (await ApiClient.LoginAsync(_enabled, "tester")).PreferLanguage("vi");
        _enabled.Model.Mode = "down";

        var (_, error) = await tester.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = Requirement });

        ((string?)error["message"])!.ShouldContain("mô hình AI");
    }

    [Fact]
    public async Task Invalid_input_is_a_400_before_the_model_is_asked()
    {
        var tester = await ApiClient.LoginAsync(_enabled, "tester");
        _enabled.Model.LastBody = null;

        var (status, error) = await tester.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = "short" });
        status.ShouldBe(HttpStatusCode.BadRequest);
        error["validationErrors"]!.AsArray().Count.ShouldBeGreaterThan(0);

        (await tester.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = "a         " })).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await tester.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = Requirement, MaxSteps = 99 })).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await tester.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = new string('a', 4001) })).Status.ShouldBe(HttpStatusCode.BadRequest);
        _enabled.Model.LastBody.ShouldBeNull();
    }

    [Fact]
    public async Task Only_those_who_hold_the_permission_may_ask_and_the_text_goes_nowhere_otherwise()
    {
        _enabled.Model.LastBody = null;
        var productOwner = await ApiClient.LoginAsync(_enabled, "product.owner");
        var anonymous = ApiClient.Anonymous(_enabled);

        (await productOwner.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/status")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await productOwner.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = Requirement })).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = Requirement })).Status.ShouldBe(HttpStatusCode.Unauthorized);
        _enabled.Model.LastBody.ShouldBeNull();
    }

    [Fact]
    public async Task An_api_key_cannot_use_it()
    {
        var qaLead = await ApiClient.LoginAsync(_enabled, "qa.lead");
        var created = await qaLead.PostAsync<Automation.Dtos.ApiKeyCreatedDto>($"/api/test-case-management/api-keys", new Automation.Dtos.CreateApiKeyDto { Name = "step suggestions " + Guid.NewGuid().ToString("N")[..6] });
        var pipeline = ApiClient.WithApiKey(_enabled, created.Key);

        (await pipeline.SendExpectingErrorAsync(HttpMethod.Post, Root, new SuggestStepsInput { RequirementText = Requirement })).Status.ShouldBe(HttpStatusCode.Forbidden);
    }
}
