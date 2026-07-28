// ---- Configuration ----
// Load the local .env before anything reads EnvironmentVariables. NoClobber:
// real process/container env vars always win over the file (prod, CI, Docker).
// TraversePath: also found when the CWD is a subdirectory (EF tools, tests).
using BoilerPlateApi.Contexts;
using BoilerPlateApi.Models.Users;
using BoilerPlateApi.Services;
using BoilerPlateApi.Utilities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Text.Json.Serialization;

DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// ---- Database ----
services.AddDbContext<MainContext>(options =>
    options.UseNpgsql(EnvironmentVariables.ConnectionString));

// ---- Identity ----
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

// ---- Authentication (JWT) ----
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

// ---- CORS ----
services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(CorsHelper.IsOriginAllowed)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));

// ---- App services ----
services.AddScoped<IAuthService, AuthService>();
services.AddScoped<MailService>();

// ---- Controllers + Swagger ----
services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

services.AddEndpointsApiExplorer();


var app = builder.Build();

// Apply migrations + seed the default super admin at startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MainContext>();
    db.Database.Migrate();
    await SeedSuperAdmin(scope.ServiceProvider);
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

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
