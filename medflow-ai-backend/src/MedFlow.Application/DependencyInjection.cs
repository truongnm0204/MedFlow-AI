using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<AuthOtpService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IExaminationServiceCatalogService, ExaminationServiceCatalogService>();
        services.AddScoped<IPatientProfileService, PatientProfileService>();
        return services;
    }
}
