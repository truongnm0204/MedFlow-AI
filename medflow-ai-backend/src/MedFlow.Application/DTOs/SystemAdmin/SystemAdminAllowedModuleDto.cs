namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record SystemAdminAllowedModuleDto(
    string ModuleCode,
    string Title,
    string Description,
    string Route,
    string RequiredPermission,
    int? BadgeCount
);
