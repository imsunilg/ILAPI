using System.Text;
using ILAPI.API.Services;
using ILAPI.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
// Real secrets: user-secrets, env vars (ConnectionStrings__PostgreSQL, Jwt__Key) or appsettings.Development.local.json
builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.StartsWith("__SET_VIA") || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be set (>= 32 chars) via user-secrets or the Jwt__Key environment variable.");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ClockSkew = TimeSpan.FromSeconds(30),
        RoleClaimType = "role",
        NameClaimType = "name"
    };
});
builder.Services.AddAuthorization(o =>
{
    // one policy per permission code; the permission claims are issued by the server from the database
    foreach (var perm in ILAPI.Core.Permissions.All) o.AddPolicy(perm, p => p.RequireAuthenticatedUser().RequireClaim("permission", perm));
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// Startup connectivity check (logs result, never the connection string)
using (var scope = app.Services.CreateScope())
{
    var hc = await scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>().CheckHealthAsync();
    var log = app.Logger;
    if (hc.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy) log.LogInformation("Database connection to ILPortal OK");
    else log.LogError("Database connection to ILPortal FAILED: {Detail}", hc.Entries["postgres"].Description);
}

app.Run();

public partial class Program;

