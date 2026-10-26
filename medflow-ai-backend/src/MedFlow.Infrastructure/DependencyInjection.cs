using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Infrastructure.Identity;
using MedFlow.Infrastructure.Integrations.Notification;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<MedFlowDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<MedFlowDbContext>());

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddTransient<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
