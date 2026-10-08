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
    /// <summary>When set (see <c>TestCaseManagementTestBaseModule.MySqlEnvironmentVariable</c>) the host runs on MySQL, in a database of its own.</summary>
    private readonly string? _mySqlServer = Environment.GetEnvironmentVariable("TCM_TEST_MYSQL");
    private readonly string _mySqlDatabase = $"tcm_http_{Guid.NewGuid():N}";
    private readonly string _storageFolder = Path.Combine(Path.GetTempPath(), $"tcm-http-tests-{Guid.NewGuid():N}-files");

    /// <summary>Development is the only environment in which the host signs callers in and creates the schema.</summary>
    protected virtual string EnvironmentName => "Development";

    protected virtual void ConfigureTestHost(IWebHostBuilder builder)
    {
    }

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        if (string.IsNullOrWhiteSpace(_mySqlServer))
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databaseFile}");
        }
        else
        {
            builder.UseSetting("Host:Database", "MySql");
            builder.UseSetting("ConnectionStrings:Default", $"{_mySqlServer.TrimEnd(';')};Database={_mySqlDatabase}");
        }

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

        if (!string.IsNullOrWhiteSpace(_mySqlServer))
        {
            DropMySqlDatabase();
        }

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

    private void DropMySqlDatabase()
    {
        try
        {
            using var connection = new MySql.Data.MySqlClient.MySqlConnection($"{_mySqlServer!.TrimEnd(';')}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS `{_mySqlDatabase}`";
            command.ExecuteNonQuery();
        }
        catch
        {
            // A leftover test database is harmless.
        }
    }
}

[CollectionDefinition(Name)]
public sealed class HostCollection : ICollectionFixture<TestCaseManagementHost>
{
    public const string Name = "Sample host";
}
