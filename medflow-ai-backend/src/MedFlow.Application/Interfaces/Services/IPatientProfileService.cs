using System;
using System.Collections.Generic;
using System.Text;

using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IPatientProfileService
{
    Task<PatientProfileResponse> UpdatePersonalProfileAsync(
        Guid userId,
        UpdatePersonalProfileRequest request,
        CancellationToken ct = default);
}

