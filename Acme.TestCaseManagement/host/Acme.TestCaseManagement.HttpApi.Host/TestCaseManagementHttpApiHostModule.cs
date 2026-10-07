using Acme.TestCaseManagement.Authentication;
using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Data;
using Acme.TestCaseManagement.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.FileSystem;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Swashbuckle;

namespace Acme.TestCaseManagement;

/// <summary>
/// Sample host of the module: ABP Identity for users and roles, ABP Permission Management for grants, JWT bearer
/// authentication, SQLite, and Swagger UI. It shows how the module is hosted and is what the Angular app talks to.
/// Treat it as a starting point; a production host needs its own identity provider, database and secrets.
/// </summary>
[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpBlobStoringFileSystemModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpEntityFrameworkCoreSqliteModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpPermissionManagementDomainModule),
    typeof(AbpPermissionManagementDomainIdentityModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(TestCaseManagementApplicationModule),
    typeof(TestCaseManagementEntityFrameworkCoreModule),
    typeof(TestCaseManagementHttpApiModule))]
public class TestCaseManagementHttpApiHostModule : AbpModule
{
    public const string ApiTitle = "Test Case Management API";

    /// <summary>Configuration key: create the schema and seed roles and demo users at start-up (default: Development only).</summary>
    public const string InitializeDatabaseSetting = "Host:InitializeDatabase";

    private const string ApiRoutePrefix = "api/test-case-management/";
    private const string LoginRoute = "api/auth/login";
    private const string BearerOrApiKeyScheme = "BearerOrApiKey";

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        Configure<AbpDbContextOptions>(options =>
        {
            options.UseSqlite();
        });

        // Attachments are kept in a folder (Storage:Path, relative to the content root unless absolute). A production host
        // chooses its own provider here: database, S3, Azure Blob Storage...
        var storagePath = configuration["Storage:Path"] ?? "App_Data/attachments";
        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.ConfigureDefault(container =>
            {
                container.UseFileSystem(fileSystem =>
                {
                    fileSystem.BasePath = Path.IsPathRooted(storagePath) ? storagePath : Path.Combine(AppContext.BaseDirectory, storagePath);
                });
            });
        });

        // Permission definitions come from code; the grants are what live in the database.
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });

        context.Services.AddAbpDbContext<HostDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        // The languages of the API: error messages follow the Accept-Language header of the caller.
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Languages.Add(new LanguageInfo("en", "en", "English"));
            options.Languages.Add(new LanguageInfo("vi", "vi", "Tiếng Việt"));
        });

        ConfigureAuthentication(context, configuration);
        ConfigureSwagger(context);
    }

    private static void ConfigureAuthentication(ServiceConfigurationContext context, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        context.Services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        // Fails at start-up, not at the first request, when the secret is missing or too short.
        var key = JwtTokenService.CreateKey(jwt);

        // A request that names an API key (X-Api-Key) is authenticated as that key, any other as a bearer token.
        context.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = BearerOrApiKeyScheme;
                options.DefaultChallengeScheme = BearerOrApiKeyScheme;
            })
            .AddPolicyScheme(BearerOrApiKeyScheme, "Bearer token or API key", options =>
            {
                options.ForwardDefaultSelector = httpContext => httpContext.Request.IsApiKeyRequest()
                    ? ApiKeyDefaults.Scheme
                    : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddTestCaseManagementApiKey()
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = key,
                    NameClaimType = AbpClaimTypes.UserName,
                    RoleClaimType = AbpClaimTypes.Role,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });
    }

    private static void ConfigureSwagger(ServiceConfigurationContext context)
    {
        context.Services.AddAbpSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = ApiTitle,
                Version = "v1",
                Description = "Sample host of the Acme.TestCaseManagement module. Log in with POST /api/auth/login, then use "
                    + "Authorize and paste the accessToken.",
            });

            // The document describes the module's API and the login endpoint, not ABP's own configuration endpoints.
            options.DocInclusionPredicate((_, api) =>
                api.RelativePath?.StartsWith(ApiRoutePrefix, StringComparison.OrdinalIgnoreCase) == true
                || api.RelativePath?.Equals(LoginRoute, StringComparison.OrdinalIgnoreCase) == true);

            options.CustomSchemaIds(SchemaId);

            // Stable method names for generated clients: TestCase_GetList, SignOff_Approve, ...
            options.CustomOperationIds(api => api.ActionDescriptor is ControllerActionDescriptor action
                ? $"{action.ControllerName}_{action.ActionName}"
                : null);

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "The accessToken returned by POST /api/auth/login.",
            });
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ApiKeyDefaults.HeaderName,
                Description = "An API key created on the Automation page or with POST /api/test-case-management/api-keys. It can only publish automation results.",
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>(),
            });

            // Descriptions of DTO members come from the XML documentation of the module assemblies.
            foreach (var xmlFile in Directory.EnumerateFiles(AppContext.BaseDirectory, "Acme.TestCaseManagement.*.xml"))
            {
                options.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
            }

            // Registered after the XML comments so that they extend, rather than get overwritten by, the type summaries.
            options.DocumentFilter<ApiKeyDocumentFilter>();
            options.SchemaFilter<EnumNamesSchemaFilter>();
            options.OperationFilter<InterfaceDocumentationOperationFilter>();
        });
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var environment = context.GetEnvironment();
        var configuration = context.GetConfiguration();

        if (environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        if (configuration.GetValue(InitializeDatabaseSetting, defaultValue: environment.IsDevelopment()))
        {
            await InitializeDatabaseAsync(context.ServiceProvider, configuration);
        }

        app.UseRouting();
        app.UseAbpRequestLocalization();
        app.UseAuthentication();
        app.UseUnitOfWork();
        app.UseAuthorization();
        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", ApiTitle);
        });
        app.UseConfiguredEndpoints();
    }

    /// <summary>
    /// The module ships no migrations, so the sample host creates the schema from the model, then seeds the ABP admin
    /// account and the roles and demo users of <see cref="HostDataSeedContributor"/> from the Seed:Password setting.
    /// </summary>
    private static async Task InitializeDatabaseAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<HostDbContext>().Database.EnsureCreatedAsync();

        var password = configuration["Seed:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            // Without it ABP would create an admin account with its well-known default password.
            scope.ServiceProvider.GetRequiredService<ILogger<TestCaseManagementHttpApiHostModule>>()
                .LogWarning("Seed:Password is not configured; no accounts were created.");
            return;
        }

        await scope.ServiceProvider.GetRequiredService<IDataSeeder>().SeedAsync(
            new DataSeedContext()
                .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName, "admin@example.test")
                .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName, password));
    }

    /// <summary>
    /// Full type name, except that closed generics get a readable name (PagedResultDtoOfTestCaseDto) instead of the
    /// assembly-qualified one that the plain FullName produces.
    /// </summary>
    private static string SchemaId(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName!.Replace('+', '.');
        }

        var definition = type.GetGenericTypeDefinition().FullName!;
        var arguments = type.GetGenericArguments().Select(argument => argument.IsGenericType ? SchemaId(argument) : argument.Name);
        return definition[..definition.IndexOf('`')] + "Of" + string.Concat(arguments);
    }
}
