using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentBookingServiceTests : IDisposable
{
    private sealed class BookingTestDbContext : DbContext, IApplicationDbContext
    {
        public BookingTestDbContext(DbContextOptions<BookingTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<HospitalPolicy>();

            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User)
                    .WithOne(u => u.Patient)
                    .HasForeignKey<Patient>(p => p.UserId);
            });
            modelBuilder.Entity<PatientFamilyLink>(b =>
            {
                b.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
                b.HasOne(l => l.OwnerPatient).WithMany().HasForeignKey(l => l.OwnerPatientId);
                b.HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            });
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department).WithMany(d => d.HospitalRooms).HasForeignKey(r => r.DepartmentId);
            });
            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.User);
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
            });
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Doctor>(b =>
            {
                b.HasKey(d => d.Id);
                b.HasOne(d => d.Staff).WithOne(s => s.Doctor).HasForeignKey<Doctor>(d => d.StaffId);
                b.HasOne(d => d.Specialty).WithMany().HasForeignKey(d => d.SpecialtyId);
            });
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany(d => d.Services).HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany(sp => sp.Services).HasForeignKey(s => s.SpecialtyId);
                b.HasMany(s => s.ServicePrices).WithOne(p => p.Service).HasForeignKey(p => p.ServiceId);
            });
            modelBuilder.Entity<ServicePrice>().HasKey(p => p.Id);
            modelBuilder.Entity<DoctorServiceAuthorization>(b =>
            {
                b.HasKey(a => new { a.DoctorId, a.ServiceId });
                b.HasOne(a => a.Doctor).WithMany(d => d.ServiceAuthorizations).HasForeignKey(a => a.DoctorId);
                b.HasOne(a => a.Service).WithMany().HasForeignKey(a => a.ServiceId);
            });
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Doctor).WithMany(d => d.ExaminationSlots).HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
                b.HasOne(s => s.Room).WithMany().HasForeignKey(s => s.RoomId);
            });
            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.HasOne(a => a.Slot).WithMany(s => s.Appointments).HasForeignKey(a => a.SlotId);
                b.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId);
            });
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong AppointmentBookingServiceTests.");
    }

    private sealed class StubPolicyService : IAppointmentPolicyService
    {
        private readonly int _bookingWindowDays;
        private readonly int _bookingLeadHours;
        private readonly int _maxActiveBookings;
        private readonly int _noShowBlockDays;

        public StubPolicyService(
            int bookingWindowDays = 30,
            int bookingLeadHours = 2,
            int maxActiveBookings = 3,
            int noShowBlockDays = 30)
        {
            _bookingWindowDays = bookingWindowDays;
            _bookingLeadHours = bookingLeadHours;
            _maxActiveBookings = maxActiveBookings;
            _noShowBlockDays = noShowBlockDays;
        }

        public Task<AppointmentPolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default)
        {
            return Task.FromResult(new AppointmentPolicyDto(
                BookingWindowDays: _bookingWindowDays,
                BookingLeadHours: _bookingLeadHours,
                MaxActiveBookings: _maxActiveBookings,
                RescheduleMinimumHours: 12,
                MaxReschedules: 2,
                CancelMinimumHours: 6,
                NoShowGraceMinutes: 15,
                NoShowMaxCount: 3,
                NoShowLookbackDays: 90,
                NoShowBlockDays: _noShowBlockDays,
                LastUpdatedAt: null));
        }

        public Task<AppointmentPolicyDto> GetForAdminAsync(long userId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentPolicyDto> UpdateForAdminAsync(
            long userId,
            UpdateAppointmentPolicyRequest request,
            string? ipAddress,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class RecordingBookingLock : IAppointmentBookingLock
    {
        public List<(long PatientId, long? DoctorId, long SlotId)> Calls { get; } = new();

        public Task AcquireAsync(long patientId, long? doctorId, long slotId, CancellationToken ct = default)
        {
            Calls.Add((patientId, doctorId, slotId));
            return Task.CompletedTask;
        }
    }

    // 2026-11-02T01:00:00Z = 2026-11-02 08:00:00 giờ VN (Thứ Hai)
    private static readonly DateTimeOffset DefaultUtcNow = new(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);

    private readonly BookingTestDbContext _db;
    private readonly RecordingBookingLock _lock = new();

    public AppointmentBookingServiceTests()
    {
        var options = new DbContextOptionsBuilder<BookingTestDbContext>()
            .UseInMemoryDatabase($"AppointmentBookingTests_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new BookingTestDbContext(options);
        SeedBaseData(_db);
    }

    public void Dispose() => _db.Dispose();

    private AppointmentBookingService CreateSut(
        int windowDays = 30,
        int leadHours = 2,
        int maxActiveBookings = 3,
        int noShowBlockDays = 30)
    {
        var timeProvider = new FixedTimeProvider(DefaultUtcNow);
        return new AppointmentBookingService(
            _db,
            new DoctorCatalogService(_db, timeProvider),
            new ExaminationServiceCatalogService(_db),
            new StubPolicyService(windowDays, leadHours, maxActiveBookings, noShowBlockDays),
            _lock,
            new AuditLogger(_db),
            timeProvider);
    }

    private static void SeedBaseData(BookingTestDbContext db)
    {
        db.Departments.Add(new Department
        {
            Id = 2,
            HospitalId = 1,
            DepartmentCode = "GI",
            Name = "Khoa Tiêu hóa",
            IsActive = true,
        });

        db.HospitalRooms.Add(new HospitalRoom
        {
            Id = 1,
            HospitalId = 1,
            DepartmentId = 2,
            RoomCode = "RM-001",
            Name = "Khám Tiêu hóa 101",
            RoomType = "EXAMINATION",
            Location = "Tầng 1, Khu Demo",
            Capacity = 2,
            Status = "ACTIVE",
        });

        db.Specialties.Add(new Specialty
        {
            Id = 1,
            SpecialtyCode = "GI",
            Name = "Tiêu hóa",
            IsActive = true,
        });

        db.Staff.Add(new Staff
        {
            Id = 1,
            UserId = 3,
            HospitalId = 1,
            DepartmentId = 2,
            StaffCode = "STF-001",
            FullName = "BS Nguyễn Minh Đức",
            Status = "ACTIVE",
        });

        db.Doctors.Add(new Doctor
        {
            Id = 1,
            StaffId = 1,
            HospitalId = 1,
            SpecialtyId = 1,
            DoctorCode = "DOC-001",
            ProfessionalTitle = "BS.CKI",
            LicenseNumber = "CCHN-1001",
            YearsOfExperience = 10,
            IsActive = true,
        });

        db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = "EX-GI-01",
                Name = "Khám chuyên khoa Tiêu hóa",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            },
            new Service
            {
                Id = 3,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = "EX-GI-02",
                Name = "Khám tái khám Tiêu hóa",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            });

        db.ServicePrices.AddRange(
            new ServicePrice { Id = 1, ServiceId = 1, PriceAmount = 300_000m, EffectiveFrom = new DateTime(2026, 1, 1) },
            new ServicePrice { Id = 2, ServiceId = 3, PriceAmount = 200_000m, EffectiveFrom = new DateTime(2026, 1, 1) });

        db.DoctorServiceAuthorizations.Add(new DoctorServiceAuthorization
        {
            DoctorId = 1,
            ServiceId = 1,
            IsActive = true,
        });

        var user1 = new User
        {
            Id = 1,
            Email = "patient01@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        };

        var patientSelf = new Patient
        {
            Id = 1,
            UserId = 1,
            PatientCode = "PAT-0001",
            FullName = "Nguyễn Văn An",
            DateOfBirth = new DateOnly(1990, 5, 15),
            Gender = "MALE",
            Phone = "0901234567",
            IdentificationHash = "HASH-001",
            Status = "ACTIVE",
        };

        var patientChild = new Patient
        {
            Id = 3,
            PatientCode = "PAT-0003",
            FullName = "Lê Minh Chi",
            DateOfBirth = new DateOnly(2018, 6, 1),
            Gender = "FEMALE",
            Status = "ACTIVE",
        };

        var patientRevoked = new Patient
        {
            Id = 4,
            PatientCode = "PAT-0004",
            FullName = "Nguyễn Văn B",
            DateOfBirth = new DateOnly(1960, 1, 1),
            Gender = "MALE",
            Status = "ACTIVE",
        };

        db.Users.Add(user1);
        db.Patients.AddRange(patientSelf, patientChild, patientRevoked);

        db.PatientFamilyLinks.AddRange(
            new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 3,
                Relationship = "CHILD",
                VerificationStatus = "VERIFIED",
                VerifiedAt = new DateTime(2026, 1, 1, 8, 0, 0),
                RevokedAt = null,
            },
            new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 4,
                Relationship = "PARENT",
                VerificationStatus = "VERIFIED",
                VerifiedAt = new DateTime(2026, 1, 1, 8, 0, 0),
                RevokedAt = new DateTime(2026, 10, 1, 8, 0, 0),
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task CreateAsync_DoctorModeSelf_PersistsAppointmentAuditAndCallsLock()
    {
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 101,
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
            EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
            Capacity = 3,
            Status = "OPEN",
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var result = await sut.CreateAsync(
            userId: 1,
            new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                ServiceCode = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "101",
                SymptomNote = "  Đau thượng vị sau ăn  ",
            },
            ipAddress: "127.0.0.1");

        var apptId = long.Parse(result.AppointmentId, CultureInfo.InvariantCulture);
        Assert.Equal($"APT-202611-{apptId:D4}", result.AppointmentCode);
        Assert.Equal("001", result.QueueNumber);
        Assert.Equal("BOOKED", result.Status);

        var persisted = await _db.Appointments.SingleAsync(a => a.Id == apptId);
        Assert.Equal("ONLINE", persisted.BookingChannel);
        Assert.Equal(1L, persisted.BookedByUserId);
        Assert.Equal("Đau thượng vị sau ăn", persisted.ReasonForVisit);
        Assert.Equal("BOOKED", persisted.Status);

        var audit = await _db.AuditLogs.SingleAsync();
        Assert.Equal("APPOINTMENT_BOOK", audit.Action);
        Assert.Equal("APPOINTMENT", audit.EntityType);
        Assert.Equal(result.AppointmentId, audit.EntityId);

        var lockCall = Assert.Single(_lock.Calls);
        Assert.Equal((1L, (long?)1L, 101L), lockCall);
    }

    [Fact]
    public async Task CreateAsync_VerifiedFamilyMemberSucceeds_NonOwnedThrowsForbiddenWithoutLock()
    {
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 102,
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 11, 3, 10, 0, 0),
            EndsAt = new DateTime(2026, 11, 3, 10, 30, 0),
            Capacity = 3,
            Status = "OPEN",
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var familyResult = await sut.CreateAsync(
            userId: 1,
            new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "3",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "102",
            },
            ipAddress: null);

        Assert.Equal("BOOKED", familyResult.Status);
        Assert.Single(_lock.Calls);

        _lock.Calls.Clear();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.CreateAsync(
                userId: 1,
                new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "4",
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    DateIso = "2026-11-03",
                    SlotOrShiftId = "102",
                },
                ipAddress: null));

        Assert.Equal("Bạn không có quyền đặt lịch khám cho hồ sơ không thuộc quyền quản lý.", ex.Message);
        Assert.Empty(_lock.Calls);
        Assert.Equal(1, await _db.Appointments.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_SlotAtCapacityThrowsConflict_CancelledRowsDoNotBlock()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 103,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 8, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 8, 30, 0),
                Capacity = 1,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 104,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 8, 30, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                Capacity = 1,
                Status = "OPEN",
            });

        _db.Appointments.AddRange(
            new Appointment { Id = 1, SlotId = 103, PatientId = 99, Status = "BOOKED" },
            new Appointment { Id = 2, SlotId = 104, PatientId = 99, Status = "CANCELLED" });
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var fullEx = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(
                userId: 1,
                new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "1",
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    DateIso = "2026-11-03",
                    SlotOrShiftId = "103",
                },
                ipAddress: null));

        Assert.Equal("Khung giờ khám vừa hết chỗ. Vui lòng chọn khung giờ khác.", fullEx.Message);

        var okResult = await sut.CreateAsync(
            userId: 1,
            new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "104",
            },
            ipAddress: null);

        Assert.Equal("BOOKED", okResult.Status);
    }

    [Fact]
    public async Task CreateAsync_OverlappingActiveAppointmentThrowsConflict_CancelledDoesNotBlock()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 105,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 106,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 15, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 45, 0),
                Capacity = 3,
                Status = "OPEN",
            });

        var existing = new Appointment
        {
            Id = 10,
            PatientId = 1,
            SlotId = 105,
            Status = "BOOKED",
        };
        _db.Appointments.Add(existing);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var overlapEx = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(
                userId: 1,
                new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "1",
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    DateIso = "2026-11-03",
                    SlotOrShiftId = "106",
                },
                ipAddress: null));

        Assert.Equal("Hồ sơ này đã có lịch khám trùng thời gian.", overlapEx.Message);

        existing.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        var ok = await sut.CreateAsync(
            userId: 1,
            new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "106",
            },
            ipAddress: null);

        Assert.Equal("BOOKED", ok.Status);
    }

    [Fact]
    public async Task CreateAsync_MaxActiveBookingsReached_ThrowsConflict()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 107,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 108,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 4, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 4, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });

        _db.Appointments.Add(new Appointment
        {
            Id = 20,
            PatientId = 1,
            SlotId = 107,
            Status = "BOOKED",
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut(maxActiveBookings: 1);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(
                userId: 1,
                new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "1",
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    DateIso = "2026-11-04",
                    SlotOrShiftId = "108",
                },
                ipAddress: null));

        Assert.Equal("Hồ sơ này đã đạt tối đa 1 lịch khám đang hoạt động.", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_NoShowBlockEnforcedWhenActive_SucceedsWhenBlockDaysZero()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 201,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 10, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 10, 9, 30, 0),
                Capacity = 3,
                Status = "CLOSED",
            },
            new ExaminationSlot
            {
                Id = 202,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 15, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 15, 9, 30, 0),
                Capacity = 3,
                Status = "CLOSED",
            },
            new ExaminationSlot
            {
                Id = 203,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 20, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 20, 9, 30, 0),
                Capacity = 3,
                Status = "CLOSED",
            },
            new ExaminationSlot
            {
                Id = 204,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });

        _db.Appointments.AddRange(
            new Appointment { Id = 31, PatientId = 1, SlotId = 201, Status = "NO_SHOW" },
            new Appointment { Id = 32, PatientId = 1, SlotId = 202, Status = "NO_SHOW" },
            new Appointment { Id = 33, PatientId = 1, SlotId = 203, Status = "NO_SHOW" });
        await _db.SaveChangesAsync();

        var blockedSut = CreateSut(noShowBlockDays: 30);
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            blockedSut.CreateAsync(
                userId: 1,
                new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "1",
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    DateIso = "2026-11-03",
                    SlotOrShiftId = "204",
                },
                ipAddress: null));

        Assert.Equal("Hồ sơ đang bị tạm khóa đặt lịch trực tuyến do vắng mặt nhiều lần.", ex.Message);

        var unblockedSut = CreateSut(noShowBlockDays: 0);
        var result = await unblockedSut.CreateAsync(
            userId: 1,
            new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "204",
            },
            ipAddress: null);

        Assert.Equal("BOOKED", result.Status);
    }

    [Fact]
    public async Task CreateAsync_TimingWindowStatusAndMismatchRules_ThrowExpectedExceptions()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 301,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 2, 9, 0, 0), // +1h < leadHours(2)
                EndsAt = new DateTime(2026, 11, 2, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 302,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 10, 9, 0, 0), // +8d > windowDays(5)
                EndsAt = new DateTime(2026, 11, 10, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 303,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 3,
                Status = "CLOSED",
            },
            new ExaminationSlot
            {
                Id = 304,
                ServiceId = 1,
                DoctorId = 99, // mismatched doctor
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });
        await _db.SaveChangesAsync();

        var sut = CreateSut(windowDays: 5, leadHours: 2);

        var leadEx = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-02",
                SlotOrShiftId = "301",
            }, null));
        Assert.Equal("Chỉ được đặt lịch trước giờ khám tối thiểu 2 giờ.", leadEx.Message);

        var winEx = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-10",
                SlotOrShiftId = "302",
            }, null));
        Assert.Equal("Chỉ được đặt lịch trong vòng 5 ngày tới.", winEx.Message);

        var closedEx = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "303",
            }, null));
        Assert.Equal("Khung giờ khám không còn mở để đặt lịch.", closedEx.Message);

        var mismatchEx = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "304",
            }, null));
        Assert.Equal("Khung giờ khám không khớp với bác sĩ, dịch vụ hoặc ngày đã chọn.", mismatchEx.Message);
    }

    [Fact]
    public async Task CreateAsync_ServiceModeAfternoon_PicksEarliestAvailableAndThrowsWhenAllFull()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 401,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 13, 30, 0),
                EndsAt = new DateTime(2026, 11, 3, 14, 0, 0),
                Capacity = 1,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 402,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 14, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 14, 30, 0),
                Capacity = 1,
                Status = "OPEN",
            });

        _db.Appointments.Add(new Appointment
        {
            Id = 40,
            PatientId = 99,
            SlotId = 401,
            Status = "BOOKED",
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var res = await sut.CreateAsync(
            userId: 1,
            new CreateAppointmentRequest
            {
                Mode = "service",
                PatientId = "1",
                DoctorId = "placeholder-ignored",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "shift-afternoon",
            },
            ipAddress: null);

        var created = await _db.Appointments.SingleAsync(a => a.Id == long.Parse(res.AppointmentId, CultureInfo.InvariantCulture));
        Assert.Equal(402L, created.SlotId);

        var fullShiftEx = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(
                userId: 1,
                new CreateAppointmentRequest
                {
                    Mode = "service",
                    PatientId = "3",
                    ServiceId = "EX-GI-01",
                    DateIso = "2026-11-03",
                    SlotOrShiftId = "shift-afternoon",
                },
                ipAddress: null));

        Assert.Equal("Ca khám đã hết chỗ. Vui lòng chọn ca hoặc ngày khác.", fullShiftEx.Message);
    }

    [Fact]
    public async Task CreateAsync_QueueNumber_IncrementsPerDoctorDayAndNeverReusesCancelled()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 501,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 8, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 8, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 502,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 503,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 10, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 10, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var first = await sut.CreateAsync(1, new CreateAppointmentRequest
        {
            Mode = "doctor",
            PatientId = "1",
            DoctorId = "DOC-001",
            ServiceId = "EX-GI-01",
            DateIso = "2026-11-03",
            SlotOrShiftId = "501",
        }, null);
        Assert.Equal("001", first.QueueNumber);

        var second = await sut.CreateAsync(1, new CreateAppointmentRequest
        {
            Mode = "doctor",
            PatientId = "3",
            DoctorId = "DOC-001",
            ServiceId = "EX-GI-01",
            DateIso = "2026-11-03",
            SlotOrShiftId = "502",
        }, null);
        Assert.Equal("002", second.QueueNumber);

        var firstEntity = await _db.Appointments.SingleAsync(a => a.Id == long.Parse(first.AppointmentId, CultureInfo.InvariantCulture));
        firstEntity.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        var third = await sut.CreateAsync(1, new CreateAppointmentRequest
        {
            Mode = "doctor",
            PatientId = "1",
            DoctorId = "DOC-001",
            ServiceId = "EX-GI-01",
            DateIso = "2026-11-03",
            SlotOrShiftId = "503",
        }, null);
        Assert.Equal("003", third.QueueNumber);
    }

    [Fact]
    public async Task CreateAsync_InputValidationErrors_ThrowValidationExceptionWithVietnameseMessages()
    {
        var sut = CreateSut();

        var badMode = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest { Mode = "invalid" }, null));
        Assert.Equal("Hình thức đặt lịch không hợp lệ.", badMode.Message);

        var badPatient = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest { Mode = "doctor", PatientId = "pat-self" }, null));
        Assert.Equal("Vui lòng chọn hồ sơ người bệnh thăm khám.", badPatient.Message);

        var badDate = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                ServiceId = "EX-GI-01",
                DateIso = "2026-13-01",
            }, null));
        Assert.Equal("Ngày khám không hợp lệ (định dạng yyyy-MM-dd).", badDate.Message);

        var longNote = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SymptomNote = new string('a', 501),
            }, null));
        Assert.Equal("Lý do khám tối đa 500 ký tự.", longNote.Message);

        var badServiceCode = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(1, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                ServiceCode = "WRONG-CODE",
                DateIso = "2026-11-03",
                SlotOrShiftId = "101",
            }, null));
        Assert.Equal("Mã dịch vụ khám không khớp với dịch vụ đã chọn.", badServiceCode.Message);
    }
}
