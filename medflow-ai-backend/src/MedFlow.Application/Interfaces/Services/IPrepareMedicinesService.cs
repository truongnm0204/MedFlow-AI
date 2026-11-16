using MedFlow.Application.DTOs.Pharmacy;

namespace MedFlow.Application.Interfaces.Services;

public interface IPrepareMedicinesService
{
    Task<PreparePrescriptionContextDto> GetPreparationContextAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null);

    Task<PreparePrescriptionResponseDto> PreparePrescriptionAsync(
        long actorUserId,
        string idOrCode,
        PreparePrescriptionRequest request,
        CancellationToken ct = default,
        string? ipAddress = null);
}
