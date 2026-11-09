using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Departments;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.ExaminationRooms;
using MedFlow.Application.Services.ExaminationSlots;
using MedFlow.Application.Services.HospitalAdmin;
using MedFlow.Application.Services.Hospitals;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Reception;
using MedFlow.Application.Services.Services;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Application.Services.Visits;
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
        services.AddScoped<IExaminationServiceAdminService, ExaminationServiceAdminService>();
        services.AddScoped<IDoctorAdminService, DoctorAdminService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDoctorCatalogService, DoctorCatalogService>();
        services.AddScoped<IHospitalCatalogService, HospitalCatalogService>();
        services.AddScoped<IPatientProfileService, PatientProfileService>();
        services.AddScoped<IFamilyProfileService, FamilyProfileService>();
        services.AddScoped<IAppointmentPolicyService, AppointmentPolicyService>();
        services.AddScoped<IQueuePolicyService, QueuePolicyService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IExaminationRoomService, ExaminationRoomService>();
        services.AddScoped<IExaminationSlotAdminService, ExaminationSlotAdminService>();
        services.AddScoped<IAppointmentSlotService, AppointmentSlotService>();
        services.AddScoped<IAppointmentBookingService, AppointmentBookingService>();
        services.AddScoped<IAppointmentListService, AppointmentListService>();
        services.AddScoped<IAppointmentDetailService, AppointmentDetailService>();
        services.AddScoped<IAppointmentCancelService, AppointmentCancelService>();
        services.AddScoped<IAppointmentRescheduleService, AppointmentRescheduleService>();
        services.AddScoped<IAppointmentStatusTrackingService, AppointmentStatusTrackingService>();
        services.AddScoped<IAppointmentQueueTrackingService, AppointmentQueueTrackingService>();
        services.AddScoped<ISystemAdminDashboardService, SystemAdminDashboardService>();
        services.AddScoped<IHospitalAdminDashboardService, HospitalAdminDashboardService>();
        services.AddScoped<IDoctorDashboardService, DoctorDashboardService>();
        services.AddScoped<IDiagnosticDashboardService, DiagnosticDashboardService>();
        services.AddScoped<IRoleCatalogService, RoleCatalogService>();
        services.AddScoped<IPharmacyDashboardService, PharmacyDashboardService>();
        services.AddScoped<IVisitRoomTransferService, VisitRoomTransferService>();
        services.AddScoped<IVisitTicketService, VisitTicketService>();
        services.AddScoped<IVisitRoomAssignmentService, VisitRoomAssignmentService>();
        services.AddScoped<IExaminationQueueService, ExaminationQueueService>();
        services.AddScoped<IQueueStatusService, QueueStatusService>();
        services.AddScoped<IReceptionPatientSearchService, ReceptionPatientSearchService>();
        services.AddScoped<ICreatePatientAtReceptionService, CreatePatientAtReceptionService>();
        services.AddScoped<IUpdatePatientAtReceptionService, UpdatePatientAtReceptionService>();
        return services;
    }
}
