namespace MedFlow.Domain.Entities;

public class Permission
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
