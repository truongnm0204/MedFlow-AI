namespace MedFlow.Application.DTOs.Appointments;

public sealed record AppointmentReviewPreviewResponse(
    ReviewPatientInfoDto PatientInfo,
    ReviewScheduleLocationDto ScheduleLocation,
    ReviewFeeSummaryDto FeeSummary,
    ReviewReceptionPolicyDto ReceptionPolicy);
