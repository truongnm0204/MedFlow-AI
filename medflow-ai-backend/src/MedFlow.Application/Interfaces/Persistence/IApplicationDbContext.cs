using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Interfaces.Persistence;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Patient> Patients { get; }
    DbSet<AuthSession> AuthSessions { get; }
    DbSet<AuthVerificationChallenge> AuthVerificationChallenges { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Department> Departments { get; }
    DbSet<Specialty> Specialties { get; }
    DbSet<Service> Services { get; }
    DbSet<ServicePrice> ServicePrices { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
