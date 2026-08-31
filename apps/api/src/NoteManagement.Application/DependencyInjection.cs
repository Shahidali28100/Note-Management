using Microsoft.Extensions.DependencyInjection;
using NoteManagement.Application.Interfaces;
using NoteManagement.Application.Services;

namespace NoteManagement.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IHealthCheckService, HealthCheckService>();
        return services;
    }
}
