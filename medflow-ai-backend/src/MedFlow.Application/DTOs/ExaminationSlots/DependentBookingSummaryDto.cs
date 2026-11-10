namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record DependentBookingSummaryDto(
    long AppointmentId,
    string AppointmentCode,
    long? PatientId,
    string PatientName,
    string Status,
    string HandlingAction
);
