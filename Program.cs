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

services.AddScoped<IAuthService, AuthService>();
services.AddScoped<MailService>();

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
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "BoilerPlate API v1"));
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
