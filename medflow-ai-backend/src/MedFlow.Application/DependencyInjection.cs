using MedFlow.Application.Services.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthOtpService>();
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}
