using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using InteractivesApi.Contexts;
using InteractivesApi.Models.Responses;
using InteractivesApi.Models.Users;
using InteractivesApi.Services;
using InteractivesApi.Utilities;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Text.Json.Serialization;

DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// Uploads. The transport cap is a backstop, not the business rule: it sits one MB above
// MAX_UPLOAD_SIZE_MB so multipart boundary/header bytes can't push a file that is exactly at
// the limit over it, and so StorageHelper is the thing that actually reports 413.
var uploadCeiling = EnvironmentVariables.MaxUploadSizeBytes + 1024L * 1024L;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = uploadCeiling);
services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = uploadCeiling);

services.AddDbContext<MainContext>(options =>
    options.UseNpgsql(EnvironmentVariables.ConnectionString));
services
    .AddIdentity<UserApp, RoleApp>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 8;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddEntityFrameworkStores<MainContext>()
    .AddDefaultTokenProviders();

services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));

services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = EnvironmentVariables.API_BACK_URL,
            ValidAudience = EnvironmentVariables.API_BACK_URL,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(EnvironmentVariables.JWT_KEY)),
        };
    });

services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(CorsHelper.IsOriginAllowed)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));

// S3 client pointed at the SeaweedFS S3 gateway (filer -s3, port 8333).
// ForcePathStyle: SeaweedFS addresses buckets as /<bucket>/<key>, not as subdomains.
// Checksums are opt-in: the SDK's default CRC32 trailers are rejected by SeaweedFS.
services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
    new BasicAWSCredentials(EnvironmentVariables.S3_ACCESS_KEY, EnvironmentVariables.S3_SECRET_KEY),
    new AmazonS3Config
    {
        ServiceURL = EnvironmentVariables.S3_SERVICE_URL,
        AuthenticationRegion = EnvironmentVariables.S3_REGION,
        ForcePathStyle = true,
        // Presigned URLs are built from this flag, not from ServiceURL's scheme: leaving it
        // false hands out https:// links for a plaintext gateway (the SeaweedFS dev default).
        UseHttp = EnvironmentVariables.S3_SERVICE_URL.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    }));

services.AddScoped<IAuthService, AuthService>();
services.AddScoped<MailService>();
services.AddScoped<IJobService, JobService>();

// Hangfire. Job state is stored in the application's own PostgreSQL database but in a separate
// schema, so its tables never show up in — or get dropped by — the EF migrations. The schema is
// created on first use; there is nothing to add to `dotnet ef migrations`.
if (EnvironmentVariables.HANGFIRE_ENABLED)
{
    services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            connection => connection.UseNpgsqlConnection(EnvironmentVariables.ConnectionString),
            new PostgreSqlStorageOptions
            {
                SchemaName = EnvironmentVariables.HANGFIRE_SCHEMA,
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(15),
            })
        .WithJobExpirationTimeout(TimeSpan.FromDays(EnvironmentVariables.HANGFIRE_RETENTION_DAYS)));

    // Separate from the client registration above: an instance can enqueue jobs without
    // processing any, which is what lets the API scale out behind a single worker.
    if (EnvironmentVariables.HANGFIRE_SERVER_ENABLED)
    {
        services.AddHangfireServer(options =>
        {
            options.ServerName = $"{Environment.MachineName}:{Environment.ProcessId}";
            if (EnvironmentVariables.HANGFIRE_WORKER_COUNT > 0)
                options.WorkerCount = EnvironmentVariables.HANGFIRE_WORKER_COUNT;
        });
    }
}

// Both storage backends are registered; IStorageResolver picks one per call so objects stored
// before a provider switch stay reachable. STORAGE_PROVIDER is only the default.
services.AddScoped<IStorageService, LocalStorageService>();
services.AddScoped<IStorageService, S3StorageService>();
services.AddScoped<IStorageResolver, StorageResolver>();

services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

services.AddEndpointsApiExplorer();

services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BoilerPlate API",
        Version = "v1",
    });

    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the raw JWT only - the UI adds the \"Bearer \" prefix itself.",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = [],
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MainContext>();
    db.Database.Migrate();
    await SeedSuperAdmin(scope.ServiceProvider);
    await EnsureBucket(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "BoilerPlate API v1"));
}

app.UseHttpsRedirection();
app.UseCors();

// A body over the transport ceiling never reaches the action: model binding turns the failed
// form read into an opaque ProblemDetails 400 before any exception can be caught. Reject on the
// declared length instead — the body is never read — so "too big" always comes back as the same
// Response<T> envelope and 413 the storage services return just under the ceiling.
app.Use(async (context, next) =>
{
    if (context.Request.ContentLength > uploadCeiling)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await context.Response.WriteAsJsonAsync(new Response<object>
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Message = $"Fichier trop volumineux (maximum {EnvironmentVariables.MAX_UPLOAD_SIZE_MB} Mo).",
        });
        return;
    }

    await next();
});
// After UseCors so public files carry CORS headers for a frontend fetch(); nothing under
// wwwroot is private — the local backend keeps private objects outside it entirely.
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Mapped after the EF migration block above so the Hangfire schema is prepared against a database
// that already exists. The dashboard has its own Basic-auth filter — it is not covered by the JWT
// pipeline, since a browser can't attach a bearer token to a plain navigation.
if (EnvironmentVariables.HANGFIRE_ENABLED)
{
    if (EnvironmentVariables.HANGFIRE_DASHBOARD_ENABLED)
    {
        app.MapHangfireDashboard(EnvironmentVariables.HANGFIRE_DASHBOARD_PATH, new DashboardOptions
        {
            DashboardTitle = "BoilerPlate — Tâches de fond",
            Authorization = [new HangfireDashboardAuthorization(app.Environment.IsDevelopment())],
        });
    }

    RegisterRecurringJobs(app.Services);
}

app.Run();

// AddOrUpdate is idempotent and keyed by job id, so every instance can run this on boot: the
// definition is overwritten, not duplicated. Removing a job here does NOT remove it from storage —
// delete it from the dashboard (or via IRecurringJobManager.RemoveIfExists) as well.
static void RegisterRecurringJobs(IServiceProvider provider)
{
    var recurringJobs = provider.GetRequiredService<IRecurringJobManager>();

    recurringJobs.AddOrUpdate<IJobService>(
        "purge-expired-refresh-tokens",
        job => job.PurgeExpiredRefreshTokens(CancellationToken.None),
        Cron.Daily(3),
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}

static async Task SeedSuperAdmin(IServiceProvider provider)
{
    var userManager = provider.GetRequiredService<UserManager<UserApp>>();
    if (await userManager.FindByEmailAsync(EnvironmentVariables.SUPER_ADMIN_EMAIL) is not null)
        return;

    var admin = new UserApp
    {
        UserName = EnvironmentVariables.SUPER_ADMIN_EMAIL,
        Email = EnvironmentVariables.SUPER_ADMIN_EMAIL,
        EmailConfirmed = true,
        FirstName = "Super",
        LastName = "Admin",
        StatusId = HardCode.ACCOUNT_ACTIVE,
        AuthProvider = AuthProviderEnum.Local,
        DataProcessingConsent = true,
        PrivacyPolicyConsent = true,
    };

    var result = await userManager.CreateAsync(admin, EnvironmentVariables.SUPER_ADMIN_PASSWORD);
    if (result.Succeeded)
        await userManager.AddToRoleAsync(admin, HardCode.ROLE_NAME_SUPER_ADMIN);
}

// Creates the bucket on first boot. Storage is best-effort: an unreachable
// SeaweedFS logs a warning instead of taking the whole API down.
static async Task EnsureBucket(IServiceProvider provider)
{
    var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Storage");
    var s3 = provider.GetRequiredService<IAmazonS3>();
    var bucket = EnvironmentVariables.S3_BUCKET;

    try
    {
        if (await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucket))
            return;

        await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
        logger.LogInformation("Bucket S3 '{Bucket}' créé sur {Endpoint}.", bucket, EnvironmentVariables.S3_SERVICE_URL);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Stockage S3 indisponible sur {Endpoint} - bucket '{Bucket}' non vérifié.",
            EnvironmentVariables.S3_SERVICE_URL, bucket);
    }
}
