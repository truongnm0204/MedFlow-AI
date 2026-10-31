using MedFlow.Application.DTOs.Departments;

namespace MedFlow.Application.Interfaces.Services;

public interface IDepartmentService
{
    Task<DepartmentListResponseDto> GetDepartmentListAsync(GetDepartmentListRequest request, CancellationToken ct = default);
}

