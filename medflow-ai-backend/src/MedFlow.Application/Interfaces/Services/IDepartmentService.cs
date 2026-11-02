using MedFlow.Application.DTOs.Departments;

namespace MedFlow.Application.Interfaces.Services;

public interface IDepartmentService
{
    Task<DepartmentListResponseDto> GetDepartmentListAsync(GetDepartmentListRequest request, CancellationToken ct = default);
    Task<DepartmentItemDto> CreateDepartmentAsync(CreateDepartmentRequest request, CancellationToken ct = default);
    Task<DepartmentItemDto> UpdateDepartmentAsync(long id, UpdateDepartmentRequest request, CancellationToken ct = default);
}
