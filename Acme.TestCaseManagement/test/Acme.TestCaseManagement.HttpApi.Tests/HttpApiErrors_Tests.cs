using System.Net;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.Suites.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// Every operation documents the same error contract (RemoteServiceErrorResponse). These tests check that the hosted
/// module really answers that way: 404 for unknown ids, 400 with member names for invalid input, 403 with the module's
/// error code for a broken business rule.
/// </summary>
[Collection(HostCollection.Name)]
public class HttpApiErrors_Tests : IAsyncLifetime
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;
    private ApiClient _client = null!;

    public HttpApiErrors_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    public async Task InitializeAsync()
    {
        _client = await ApiClient.LoginAsync(_host, "qa.lead");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_Unknown_Id_Is_A_404_With_An_Error_Message()
    {
        var (status, error) = await _client.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/suites/{Guid.NewGuid()}");

        status.ShouldBe(HttpStatusCode.NotFound);
        ((string?)error["message"]).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Invalid_Input_Is_A_400_That_Names_The_Offending_Members()
    {
        var (status, error) = await _client.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/suites", new CreateTestSuiteDto { Name = "" });

        status.ShouldBe(HttpStatusCode.BadRequest);
        var members = error["validationErrors"]!.AsArray()
            .SelectMany(validationError => validationError!["members"]!.AsArray().Select(member => (string)member!))
            .ToList();
        members.ShouldContain(member => string.Equals(member, "Name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Values_Outside_The_Documented_Range_Are_A_400()
    {
        var (status, error) = await _client.SendExpectingErrorAsync(
            HttpMethod.Post,
            $"{Root}/quality-gates",
            new CreateUpdateQualityGateDto { Name = "Out of range", MinPassRate = 0m, RequiredApprovals = QualityGateConsts.MaxRequiredApprovals + 1 });

        status.ShouldBe(HttpStatusCode.BadRequest);
        var members = error["validationErrors"]!.AsArray()
            .SelectMany(validationError => validationError!["members"]!.AsArray().Select(member => (string)member!))
            .Select(member => member.ToLowerInvariant())
            .ToList();
        members.ShouldContain("minpassrate");
        members.ShouldContain("requiredapprovals");
    }

    [Fact]
    public async Task A_Broken_Business_Rule_Is_A_403_With_The_Error_Code_Of_The_Module()
    {
        var name = $"Gate {Guid.NewGuid():N}"[..20];
        await _client.PostAsync<QualityGateDto>(
            $"{Root}/quality-gates", new CreateUpdateQualityGateDto { Name = name, IsDefault = false });

        var (status, error) = await _client.SendExpectingErrorAsync(
            HttpMethod.Post, $"{Root}/quality-gates", new CreateUpdateQualityGateDto { Name = name.ToUpperInvariant(), IsDefault = false });

        status.ShouldBe(HttpStatusCode.Forbidden);
        ((string?)error["code"]).ShouldBe(TestCaseManagementErrorCodes.DuplicateQualityGateName);
        ((string?)error["message"]).ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("vi", "Đã tồn tại quality gate")]
    [InlineData("vi-VN", "Đã tồn tại quality gate")]
    [InlineData("en", "already exists")]
    [InlineData("fr", "already exists")] // not a language of the host: English is the fallback
    public async Task The_Business_Error_Message_Follows_The_Accept_Language_Header(string language, string expectedText)
    {
        var client = (await ApiClient.LoginAsync(_host, "qa.lead")).PreferLanguage(language);
        var name = $"Gate {Guid.NewGuid():N}"[..20];
        await client.PostAsync<QualityGateDto>($"{Root}/quality-gates", new CreateUpdateQualityGateDto { Name = name, IsDefault = false });

        var (status, error) = await client.SendExpectingErrorAsync(
            HttpMethod.Post, $"{Root}/quality-gates", new CreateUpdateQualityGateDto { Name = name.ToUpperInvariant(), IsDefault = false });

        status.ShouldBe(HttpStatusCode.Forbidden);
        ((string?)error["code"]).ShouldBe(TestCaseManagementErrorCodes.DuplicateQualityGateName);
        ((string)error["message"]!).ShouldContain(expectedText);
    }
}
