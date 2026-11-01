using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Departments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Policies;
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
        services.AddScoped<IDoctorAdminService, DoctorAdminService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDoctorCatalogService, DoctorCatalogService>();
        services.AddScoped<IPatientProfileService, PatientProfileService>();
        services.AddScoped<IAppointmentPolicyService, AppointmentPolicyService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IAppointmentSlotService, AppointmentSlotService>();
        services.AddScoped<IAppointmentBookingService, AppointmentBookingService>();
        services.AddScoped<IAppointmentListService, AppointmentListService>();
        services.AddScoped<IAppointmentDetailService, AppointmentDetailService>();
        return services;
    }
}
