using System.Net;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>The list of people the sample host offers for assigning tests.</summary>
[Collection(HostCollection.Name)]
public class HttpApiHostUsers_Tests
{
    private sealed class HostUser
    {
        public Guid Id { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }

    private readonly TestCaseManagementHost _host;

    public HttpApiHostUsers_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task A_Signed_In_User_Gets_The_Active_Users_With_Their_Ids()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");

        var users = await qaLead.GetAsync<List<HostUser>>("/api/host/users");

        users.ShouldContain(u => u.Id == qaLead.UserId && u.UserName == "qa.lead");
        users.ShouldAllBe(u => u.DisplayName.Length > 0);
    }

    [Fact]
    public async Task Nobody_Is_Listed_To_An_Anonymous_Caller()
    {
        using var response = await ApiClient.Anonymous(_host).SendRawAsync(HttpMethod.Get, "/api/host/users");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
