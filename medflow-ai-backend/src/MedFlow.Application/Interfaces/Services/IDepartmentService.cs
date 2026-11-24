using MedFlow.Application.DTOs.Departments;

namespace MedFlow.Application.Interfaces.Services;

public interface IDepartmentService
{
    Task<DepartmentListResponseDto> GetDepartmentListAsync(long actorUserId, GetDepartmentListRequest request, CancellationToken ct = default);
    Task<DepartmentItemDto> CreateDepartmentAsync(long actorUserId, CreateDepartmentRequest request, CancellationToken ct = default);
    Task<DepartmentItemDto> UpdateDepartmentAsync(long actorUserId, long id, UpdateDepartmentRequest request, CancellationToken ct = default);
    Task<DepartmentDependencySummaryDto> CheckDepartmentDependenciesAsync(long? actorUserId, string idOrCode, CancellationToken ct = default);
    Task<DeactivateDepartmentResponseDto> DeactivateDepartmentAsync(long? actorUserId, string idOrCode, DeactivateDepartmentRequest? request = null, string? ipAddress = null, CancellationToken ct = default);
    Task<DeactivateDepartmentResponseDto> ReactivateDepartmentAsync(long? actorUserId, string idOrCode, ReactivateDepartmentRequest? request = null, string? ipAddress = null, CancellationToken ct = default);
}
