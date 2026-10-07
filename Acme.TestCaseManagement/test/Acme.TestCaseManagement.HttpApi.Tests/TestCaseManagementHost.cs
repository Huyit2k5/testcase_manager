using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// Starts the sample host in-process on a throw-away SQLite file, so that tests go through routing, model binding,
/// the ABP pipeline (unit of work, validation, exception mapping) and JSON serialization like a real client.
/// </summary>
public class TestCaseManagementHost : WebApplicationFactory<Program>
{
    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"tcm-http-tests-{Guid.NewGuid():N}.db");
    private readonly string _storageFolder = Path.Combine(Path.GetTempPath(), $"tcm-http-tests-{Guid.NewGuid():N}-files");

    /// <summary>Development is the only environment in which the host signs callers in and creates the schema.</summary>
    protected virtual string EnvironmentName => "Development";

    protected virtual void ConfigureTestHost(IWebHostBuilder builder)
    {
    }

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databaseFile}");
        builder.UseSetting("Storage:Path", _storageFolder);
        ConfigureTestHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // Pooled connections keep the file locked on Windows.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_storageFolder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }

        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_databaseFile) + "*"))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless.
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class HostCollection : ICollectionFixture<TestCaseManagementHost>
{
    public const string Name = "Sample host";
}
