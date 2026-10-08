using Acme.TestCaseManagement.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Autofac;
using Volo.Abp.BlobStoring;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.MySQL;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpTestBaseModule),
    typeof(AbpEntityFrameworkCoreSqliteModule),
    typeof(AbpEntityFrameworkCoreMySQLModule),
    typeof(TestCaseManagementApplicationModule),
    typeof(TestCaseManagementEntityFrameworkCoreModule))]
public class TestCaseManagementTestBaseModule : AbpModule
{
    /// <summary>
    /// Environment variable that points the tests at a MySQL server instead of the in-memory SQLite database, for example
    /// <c>Server=localhost;Port=3307;User ID=root;Password=...</c> (no database name). One database is created for the whole run and
    /// emptied before each test application starts, so the tests must not run in parallel: use <c>test/mysql.runsettings</c>.
    /// </summary>
    public const string MySqlEnvironmentVariable = "TCM_TEST_MYSQL";

    private static readonly object MySqlGate = new();
    private static string? _mySqlDatabase;

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Permission checks are covered by the foundation tests; feature tests run as an authorized user.
        context.Services.AddAlwaysAllowAuthorization();

        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.ConfigureDefault(container => container.ProviderType = typeof(InMemoryBlobProvider));
        });

        var server = Environment.GetEnvironmentVariable(MySqlEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(server))
        {
            var connectionString = PrepareMySqlDatabase(server);

            Configure<AbpDbContextOptions>(options =>
            {
                options.UseMySQL();
                options.Configure(configurationContext =>
                {
                    configurationContext.DbContextOptions.UseMySQL(connectionString);
                });
            });
            return;
        }

        var sqliteConnection = CreateDatabaseAndGetConnection();

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(configurationContext =>
            {
                configurationContext.DbContextOptions.UseSqlite(sqliteConnection);
            });
        });
    }

    /// <summary>Creates the schema the first time (and drops the database when the process ends), and empties every table each time.</summary>
    private static string PrepareMySqlDatabase(string server)
    {
        lock (MySqlGate)
        {
            var created = _mySqlDatabase == null;
            _mySqlDatabase ??= $"tcm_test_{Environment.ProcessId}";
            var connectionString = $"{server.TrimEnd(';')};Database={_mySqlDatabase}";

            using var dbContext = new TestCaseManagementDbContext(
                new DbContextOptionsBuilder<TestCaseManagementDbContext>().UseMySQL(connectionString).Options);

            if (created)
            {
                dbContext.Database.EnsureDeleted();
                dbContext.Database.EnsureCreated();
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try
                    {
                        using var last = new TestCaseManagementDbContext(
                            new DbContextOptionsBuilder<TestCaseManagementDbContext>().UseMySQL(connectionString).Options);
                        last.Database.EnsureDeleted();
                    }
                    catch
                    {
                        // A leftover test database is harmless.
                    }
                };
            }
            else
            {
                // One connection for the whole statement list: the session setting must stay on.
                var connection = dbContext.Database.GetDbConnection();
                connection.Open();
                try
                {
                    var tables = new List<string>();
                    using (var query = connection.CreateCommand())
                    {
                        query.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE()";
                        using var reader = query.ExecuteReader();
                        while (reader.Read())
                        {
                            tables.Add(reader.GetString(0));
                        }
                    }

                    using var clear = connection.CreateCommand();
                    clear.CommandText = "SET FOREIGN_KEY_CHECKS = 0;" + string.Concat(tables.Select(t => $"DELETE FROM `{t}`;")) + "SET FOREIGN_KEY_CHECKS = 1;";
                    clear.ExecuteNonQuery();
                }
                finally
                {
                    connection.Close();
                }
            }

            return connectionString;
        }
    }

    private static SqliteConnection CreateDatabaseAndGetConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestCaseManagementDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new TestCaseManagementDbContext(options))
        {
            context.GetService<IRelationalDatabaseCreator>().CreateTables();
        }

        return connection;
    }
}
