namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record GetRoleListRequest(
    string? Keyword = null,
    string? Query = null,
    string? Scope = null,
    string? Status = null,
    bool? IsSystemRole = null,
    int Page = 1,
    int PageSize = 10
);
