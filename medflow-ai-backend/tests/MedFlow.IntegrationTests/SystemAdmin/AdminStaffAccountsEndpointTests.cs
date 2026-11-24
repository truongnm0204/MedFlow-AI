using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminStaffAccountsEndpointTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AdminStaffAccountsEndpointTests(TestWebAppFactory factory)
    {
        var databaseName = $"AdminStaffAccountsEndpointTests_{Guid.NewGuid():N}";
        var seedOptions = new DbContextOptionsBuilder<IntegrationStaffDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        using (var seedContext = new IntegrationStaffDbContext(seedOptions))
        {
            Seed(seedContext);
        }

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationDbContext>();
                services.AddDbContext<IntegrationStaffDbContext>(
                    options => options.UseInMemoryDatabase(databaseName));
                services.AddScoped<IApplicationDbContext>(
                    provider => provider.GetRequiredService<IntegrationStaffDbContext>());
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Create_Unauthenticated_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/staff-accounts",
            ValidRequest("STF-9000", UniqueEmail()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_NonSystemAdminRoleClaim_Returns403()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");
        using var request = AuthorizedRequest(
            token,
            ValidRequest("STF-9001", UniqueEmail()));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ForgedSystemAdminClaimForPatient_Returns403()
    {
        var token = GenerateToken(1, "patient01@medflow.test", "SYSTEM_ADMIN");
        using var request = AuthorizedRequest(
            token,
            ValidRequest("STF-9002", UniqueEmail()));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidSystemAdmin_Returns201AndPersistsAtomicAccount()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");
        var email = UniqueEmail();
        using var request = AuthorizedRequest(token, ValidRequest("STF-0250", email));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateStaffAccountResponse>();
        Assert.NotNull(body);
        Assert.Equal(email, body.Email);
        Assert.Equal("RECEPTION", body.RoleCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == body.UserId);
        var staff = await db.Staff.AsNoTracking().SingleAsync(s => s.Id == body.StaffId);
        var roleCode = await db.UserRoles
            .Where(ur => ur.UserId == body.UserId)
            .Join(db.Roles, ur => ur.RoleId, role => role.Id, (_, role) => role.Code)
            .SingleAsync();
        var audit = await db.AuditLogs
            .AsNoTracking()
            .SingleAsync(a =>
                a.Action == "STAFF_ACCOUNT_CREATED" &&
                a.EntityId == body.StaffId.ToString());

        Assert.Equal(user.Id, staff.UserId);
        Assert.Equal("RECEPTION", roleCode);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash!, "MedFlow9A"));
        Assert.DoesNotContain("MedFlow9A", audit.ChangeData, StringComparison.Ordinal);
        Assert.DoesNotContain("password", audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.Email, audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(staff.FullName, audit.ChangeData, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_GoogleAccountThenLocalLogin_ReturnsGeneric401()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");
        var email = UniqueEmail();
        var payload = ValidRequest("STF-0251", email) with
        {
            AuthProvider = "GOOGLE",
            Password = null,
        };
        using var createRequest = AuthorizedRequest(token, payload);

        var createResponse = await _client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = email, Password = "Arbitrary1" });

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(
            new User { Id = userId, Email = email },
            roles).AccessToken;
    }

    private static HttpRequestMessage AuthorizedRequest(
        string token,
        CreateStaffAccountRequest payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/staff-accounts")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static CreateStaffAccountRequest ValidRequest(string staffCode, string email) => new()
    {
        StaffCode = staffCode,
        FullName = "Nhân sự kiểm thử",
        AuthProvider = "LOCAL",
        Facility = "HOS-DEMO-01",
        Email = email,
        Password = "MedFlow9A",
        Status = "ACTIVE",
        Role = "RECEPTIONIST",
        Department = "OPD",
    };

    private static string UniqueEmail() => $"staff-{Guid.NewGuid():N}@medflow.test";

    private static void Seed(IntegrationStaffDbContext db)
    {
        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Nhân viên tiếp đón" },
            new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" });

        db.Permissions.Add(new Permission { Id = 12, Code = "USER_MANAGE" });
        db.RolePermissions.Add(new RolePermission { RoleId = 7, PermissionId = 12 });

        db.Users.AddRange(
            new User { Id = 1, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "system_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 13, RoleId = 7 });

        db.Hospitals.Add(new Hospital
        {
            Id = 1,
            HospitalCode = "HOS-DEMO-01",
            Name = "Bệnh viện MedFlow",
            IsActive = true,
        });
        db.Departments.Add(new Department
        {
            Id = 1,
            HospitalId = 1,
            DepartmentCode = "OPD",
            Name = "Khoa Khám bệnh",
            IsActive = true,
        });
        db.Staff.Add(new Staff
        {
            Id = 13,
            UserId = 13,
            HospitalId = 1,
            DepartmentId = 1,
            StaffCode = "STF-0013",
            FullName = "Quản trị hệ thống",
            Status = "ACTIVE",
        });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    public sealed class IntegrationStaffDbContext(
        DbContextOptions<IntegrationStaffDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        private static NotSupportedException NotUsed() =>
            new("Not needed for staff account endpoint tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            Task.FromResult<IDbContextTransaction>(new NoopTransaction());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Property(u => u.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);

            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Role>().Ignore(r => r.RolePermissions);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<Permission>().Ignore(p => p.RolePermissions);
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<RolePermission>().Ignore(rp => rp.Role);
            modelBuilder.Entity<RolePermission>().Ignore(rp => rp.Permission);

            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId);
            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId);

            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>()
                .HasOne(p => p.User)
                .WithOne(u => u.Patient)
                .HasForeignKey<Patient>(p => p.UserId);

            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().Property(s => s.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Staff>().Ignore(s => s.Department);
            modelBuilder.Entity<Staff>().Ignore(s => s.Doctor);
            modelBuilder.Entity<Staff>()
                .HasOne(s => s.User)
                .WithOne(u => u.Staff)
                .HasForeignKey<Staff>(s => s.UserId);

            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Department>().Ignore(d => d.Services);
            modelBuilder.Entity<Department>().Ignore(d => d.HospitalRooms);

            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Property(a => a.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<AuditLog>().Ignore(a => a.ActorUser);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
        }
    }

    private sealed class NoopTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public bool SupportsSavepoints => false;

        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void CreateSavepoint(string name) => throw new NotSupportedException();
        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void RollbackToSavepoint(string name) => throw new NotSupportedException();
        public Task RollbackToSavepointAsync(
            string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ReleaseSavepoint(string name) => throw new NotSupportedException();
        public Task ReleaseSavepointAsync(
            string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
