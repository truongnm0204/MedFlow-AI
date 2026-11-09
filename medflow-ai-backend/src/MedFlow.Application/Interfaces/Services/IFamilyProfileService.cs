using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IFamilyProfileService
{
    Task<IReadOnlyList<FamilyMemberResponse>> GetFamilyMembersAsync(
        long userId,
        bool includeSelf = true,
        CancellationToken ct = default);

    Task<FamilyMemberResponse> GetFamilyMemberDetailAsync(
        long userId,
        string identifier,
        CancellationToken ct = default);
}
