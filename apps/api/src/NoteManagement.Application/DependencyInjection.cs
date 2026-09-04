using Microsoft.Extensions.DependencyInjection;
using NoteManagement.Application.Interfaces;
using NoteManagement.Application.Services;

namespace NoteManagement.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IHealthCheckService, HealthCheckService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ISearchService, SearchService>();
        return services;
    }
}
