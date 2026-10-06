using Acme.TestCaseManagement;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseAutofac();
await builder.AddApplicationAsync<TestCaseManagementHttpApiHostModule>();

var app = builder.Build();
await app.InitializeApplicationAsync();
await app.RunAsync();

// Public so that the HTTP tests can start this host through WebApplicationFactory<Program>.
public partial class Program
{
}
