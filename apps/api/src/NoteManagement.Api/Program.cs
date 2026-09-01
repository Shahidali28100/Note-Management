using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NoteManagement.Api.Middleware;
using NoteManagement.Application;
using NoteManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// AB-1004 (user-requested fix): Swagger UI has had no Authorize button since AB-1002 introduced
// the first [Authorize] endpoint — AddSwaggerGen() never registered a security scheme. Global
// AddSecurityRequirement (not per-endpoint) means every operation in the doc shows a lock icon,
// including [AllowAnonymous] ones (register/login/refresh/logout/forgot-password/reset-password)
// — cosmetic only, actual authorization is still enforced entirely server-side by each action's
// [Authorize]/[AllowAnonymous] attribute (SDS §29/AGENTS.md §7).
builder.Services.AddSwaggerGen(options =>
{
    // Swashbuckle.AspNetCore v10's public API mirrors Microsoft.OpenApi 2.x — types moved from
    // Microsoft.OpenApi.Models to Microsoft.OpenApi, and AddSecurityRequirement now takes a
    // Func<OpenApiDocument, OpenApiSecurityRequirement> so the reference can point at the
    // just-defined scheme within the document being built (verified against the v10 migration
    // guide and BearerAuthentication sample, both under domaindrivendev/swashbuckle.aspnetcore).
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the access token returned by POST /api/auth/login or /api/auth/refresh (no \"Bearer \" prefix — Swashbuckle adds it).",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

// Consistent error contract (SDS §39, ASP.NET Core Problem Details) for every controller,
// not just this ticket's health check. AB-1002's typed-exception handler is registered ahead
// of the generic AddProblemDetails() so it gets first refusal on known exceptions.
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();

// AB-1002: JWT bearer authentication. Read independently from Infrastructure's own JwtOptions
// (which builds the token *generator*) — a small, deliberate duplication of 3 config reads
// rather than sharing an IOptions<> binding across a layer boundary that doesn't need it.
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException(
        "Configuration 'Jwt:SigningKey' not found. Copy appsettings.Development.json.example to appsettings.Development.json and fill it in.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Configuration 'Jwt:Issuer' not found.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Configuration 'Jwt:Audience' not found.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET Core remaps the standard "sub" claim to the legacy
        // ClaimTypes.NameIdentifier URI, so a literal JwtRegisteredClaimNames.Sub lookup
        // (AuthController.GetMe, and every future [Authorize] action) would find nothing.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        };
    });
builder.Services.AddAuthorization();

// No wildcard CORS (SDS §65) — restricted to the configured frontend origin.
const string frontendCorsPolicy = "Frontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendCorsPolicy, policy => policy
        .WithOrigins(builder.Configuration.GetValue<string>("Cors:FrontendOrigin") ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Each layer self-registers via its own DependencyInjection.cs; only Program.cs composes them.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(frontendCorsPolicy);
app.UseAuthentication(); // NEW — must precede UseAuthorization()
app.UseAuthorization();
app.MapControllers();

app.Run();

// Required so NoteManagement.Tests.Integration can target this host via WebApplicationFactory<Program>.
public partial class Program
{
}
