using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Hospitals;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Hospitals;

public sealed class HospitalCatalogServiceTests : IDisposable
{
    private sealed class HospitalTestDbContext : DbContext, IApplicationDbContext
    {
        public HospitalTestDbContext(DbContextOptions<HospitalTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();

        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => throw NotUsed();
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

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<User>();
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<Staff>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Department>();
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

            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong HospitalCatalogServiceTests.");
    }

    private readonly HospitalTestDbContext _db;
    private readonly HospitalCatalogService _sut;

    public HospitalCatalogServiceTests()
    {
        var options = new DbContextOptionsBuilder<HospitalTestDbContext>()
            .UseInMemoryDatabase($"hospitals-{Guid.NewGuid():N}")
            .Options;

        _db = new HospitalTestDbContext(options);
        _sut = new HospitalCatalogService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ListAsync_OrdersActiveFirstThenByNameAndId_AndMapsNullIsActiveToTrue()
    {
        _db.Hospitals.AddRange(
            new Hospital
            {
                Id = 2,
                HospitalCode = "HOS-INACTIVE",
                Name = "A Cơ sở Tạm ngưng",
                Address = "10 Đường A, Đà Nẵng",
                IsActive = false,
            },
            new Hospital
            {
                Id = 3,
                HospitalCode = "HOS-NULL",
                Name = "Bệnh viện MedFlow Cơ sở B",
                Address = null,
                IsActive = null,
            },
            new Hospital
            {
                Id = 1,
                HospitalCode = "HOS-A",
                Name = "Bệnh viện MedFlow Cơ sở A",
                Address = "Số 1 Đường Demo, Hà Nội",
                IsActive = true,
            });
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync();

        Assert.Equal(3, result.Count);

        Assert.Equal(1, result[0].Id);
        Assert.Equal("HOS-A", result[0].HospitalCode);
        Assert.Equal("Bệnh viện MedFlow Cơ sở A", result[0].Name);
        Assert.Equal("Số 1 Đường Demo, Hà Nội", result[0].Address);
        Assert.True(result[0].IsActive);

        Assert.Equal(3, result[1].Id);
        Assert.Equal("HOS-NULL", result[1].HospitalCode);
        Assert.Equal("Bệnh viện MedFlow Cơ sở B", result[1].Name);
        Assert.Null(result[1].Address);
        Assert.True(result[1].IsActive);

        Assert.Equal(2, result[2].Id);
        Assert.Equal("HOS-INACTIVE", result[2].HospitalCode);
        Assert.Equal("A Cơ sở Tạm ngưng", result[2].Name);
        Assert.Equal("10 Đường A, Đà Nẵng", result[2].Address);
        Assert.False(result[2].IsActive);
    }
}
