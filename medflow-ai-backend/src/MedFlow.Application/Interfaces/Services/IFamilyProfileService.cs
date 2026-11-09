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

    Task<FamilyMemberResponse> CreateFamilyMemberAsync(
        long userId,
        CreateFamilyMemberRequest request,
        CancellationToken ct = default);

    Task<FamilyMemberResponse> UpdateFamilyMemberAsync(
        long userId,
        string identifier,
        UpdateFamilyMemberRequest request,
        CancellationToken ct = default);

    Task RemoveFamilyMemberAsync(
        long userId,
        string identifier,
        string? reason = null,
        CancellationToken ct = default);
}
