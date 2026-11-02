namespace MedFlow.Application.DTOs.Appointments;

/// <summary>Request body cho PATCH /api/v1/appointments/{idOrCode}/reason.</summary>
public sealed record UpdateAppointmentReasonRequest(string? ReasonForVisit);
