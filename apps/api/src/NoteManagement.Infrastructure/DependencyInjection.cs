using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoteManagement.Application.Interfaces;
using NoteManagement.Infrastructure.Authentication;
using NoteManagement.Infrastructure.Data;
using NoteManagement.Infrastructure.HealthChecks;
using NoteManagement.Infrastructure.Repositories;

namespace NoteManagement.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found. Copy appsettings.Development.json.example to appsettings.Development.json and fill it in.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IDatabaseHealthChecker, DatabaseHealthChecker>();

        // AB-1002: auth persistence + crypto primitives.
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        // AB-1003: password-reset OTP persistence + generation.
        services.AddScoped<IPasswordResetOtpRepository, PasswordResetOtpRepository>();
        services.AddSingleton<IOtpGenerator, OtpGenerator>(); // stateless, same treatment as IRefreshTokenSecretService

        // AB-1004: notes persistence.
        services.AddScoped<INoteRepository, NoteRepository>();

        var jwtSigningKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "Configuration 'Jwt:SigningKey' not found. Copy appsettings.Development.json.example to appsettings.Development.json and fill it in.");
        var jwtIssuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Configuration 'Jwt:Issuer' not found.");
        var jwtAudience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Configuration 'Jwt:Audience' not found.");
        var jwtOptions = new JwtOptions(jwtSigningKey, jwtIssuer, jwtAudience, TimeSpan.FromMinutes(15));

        // Stateless once JwtOptions is built — safe as singletons.
        services.AddSingleton<IJwtTokenGenerator>(new JwtTokenGenerator(jwtOptions));
        services.AddSingleton<IRefreshTokenSecretService, RefreshTokenSecretService>();

        return services;
    }
}
