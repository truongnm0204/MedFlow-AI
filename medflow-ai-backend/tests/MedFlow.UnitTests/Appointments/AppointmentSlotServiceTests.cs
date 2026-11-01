using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentSlotServiceTests : IDisposable
{
    private sealed class SlotTestDbContext : DbContext, IApplicationDbContext
    {
        public SlotTestDbContext(DbContextOptions<SlotTestDbContext> options)
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

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
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
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<HospitalPolicy>();

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
            => new("DbSet này không được dùng trong AppointmentSlotServiceTests.");
    }

    private sealed class StubPolicyService : IAppointmentPolicyService
    {
        private readonly int _bookingWindowDays;
        private readonly int _bookingLeadHours;

        public StubPolicyService(int bookingWindowDays = 30, int bookingLeadHours = 2)
        {
            _bookingWindowDays = bookingWindowDays;
            _bookingLeadHours = bookingLeadHours;
        }

        public Task<AppointmentPolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default)
        {
            return Task.FromResult(new AppointmentPolicyDto(
                BookingWindowDays: _bookingWindowDays,
                BookingLeadHours: _bookingLeadHours,
                MaxActiveBookings: 3,
                RescheduleMinimumHours: 12,
                MaxReschedules: 2,
                CancelMinimumHours: 6,
                NoShowGraceMinutes: 15,
                NoShowMaxCount: 3,
                NoShowLookbackDays: 90,
                NoShowBlockDays: 30,
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

    // 2026-11-02T01:00:00Z = 2026-11-02 08:00:00 giờ VN (Thứ Hai)
    private static readonly DateTimeOffset DefaultUtcNow = new(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);

    private readonly SlotTestDbContext _db;

    public AppointmentSlotServiceTests()
    {
        var options = new DbContextOptionsBuilder<SlotTestDbContext>()
            .UseInMemoryDatabase($"AppointmentSlotTests_{Guid.NewGuid():N}")
            .Options;

        _db = new SlotTestDbContext(options);
        SeedBaseData(_db);
    }

    public void Dispose() => _db.Dispose();

    private AppointmentSlotService CreateSut(int windowDays = 30, int leadHours = 2)
    {
        return new AppointmentSlotService(
            _db,
            new DoctorCatalogService(_db),
            new ExaminationServiceCatalogService(_db),
            new StubPolicyService(windowDays, leadHours),
            new FixedTimeProvider(DefaultUtcNow));
    }

    private static void SeedBaseData(SlotTestDbContext db)
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

        // Chỉ authorize (1, 1); service 3 không được authorize cho doctor 1
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
    public async Task GetAvailableSlotsAsync_WithBookedAndCancelledAppointments_ComputesRemainingAndStatuses()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 101,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 102,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                EndsAt = new DateTime(2026, 11, 3, 10, 0, 0),
                Capacity = 2,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 103,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 10, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 10, 30, 0),
                Capacity = 1,
                Status = "OPEN",
            });

        _db.Appointments.AddRange(
            new Appointment { Id = 1, SlotId = 101, PatientId = 1, Status = "BOOKED" },
            new Appointment { Id = 2, SlotId = 101, PatientId = 3, Status = "CANCELLED" },
            new Appointment { Id = 3, SlotId = 102, PatientId = 1, Status = "BOOKED" },
            new Appointment { Id = 4, SlotId = 103, PatientId = 1, Status = "BOOKED" });
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var result = await sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
        {
            DoctorId = "DOC-001",
            ServiceId = "EX-GI-01",
            DateIso = "2026-11-03",
        });

        Assert.Equal(3, result.MorningSlots.Count);

        var slot101 = Assert.Single(result.MorningSlots, s => s.Id == "101");
        Assert.Equal(2, slot101.RemainingSlots);
        Assert.Equal("AVAILABLE", slot101.Status);
        Assert.Equal("Còn 2 slot", slot101.BadgeText);

        var slot102 = Assert.Single(result.MorningSlots, s => s.Id == "102");
        Assert.Equal(1, slot102.RemainingSlots);
        Assert.Equal("LAST_SLOT", slot102.Status);
        Assert.Equal("Còn 1 slot", slot102.BadgeText);

        var slot103 = Assert.Single(result.MorningSlots, s => s.Id == "103");
        Assert.Equal(0, slot103.RemainingSlots);
        Assert.Equal("FULL", slot103.Status);
        Assert.Equal("Hết chỗ", slot103.BadgeText);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_LeadHoursFilter_ExcludesSlotWithinLeadTime()
    {
        // Now = 02/11 08:00, lead = 2h => min start = 02/11 10:00
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 201,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 2, 9, 30, 0),
                EndsAt = new DateTime(2026, 11, 2, 10, 0, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 202,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 2, 10, 0, 0),
                EndsAt = new DateTime(2026, 11, 2, 10, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });
        await _db.SaveChangesAsync();

        var sut = CreateSut(windowDays: 30, leadHours: 2);
        var result = await sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
        {
            DoctorId = "doc-01",
            DateIso = "2026-11-02",
        });

        var single = Assert.Single(result.MorningSlots);
        Assert.Equal("202", single.Id);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_BookingWindowDays_ExcludesSlotsBeyondWindow()
    {
        // Today = 02/11, window = 3 days => max date = 05/11; slot 06/11 bị loại
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 301,
            DoctorId = 1,
            ServiceId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 11, 6, 9, 0, 0),
            EndsAt = new DateTime(2026, 11, 6, 9, 30, 0),
            Capacity = 3,
            Status = "OPEN",
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut(windowDays: 3, leadHours: 2);
        var result = await sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
        {
            DoctorId = "DOC-001",
            DateIso = "2026-11-06",
        });

        var day06 = Assert.Single(result.Dates, d => d.DateIso == "2026-11-06");
        Assert.Equal("OFF", day06.Status);
        Assert.Empty(result.MorningSlots);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_ClosedSlot_ExcludedAndDayIsOff()
    {
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 401,
            DoctorId = 1,
            ServiceId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
            EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
            Capacity = 3,
            Status = "CLOSED",
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var result = await sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
        {
            DoctorId = "DOC-001",
            DateIso = "2026-11-03",
        });

        var day03 = Assert.Single(result.Dates, d => d.DateIso == "2026-11-03");
        Assert.Equal("OFF", day03.Status);
        Assert.Empty(result.MorningSlots);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_SevenDayStrip_FormatsVietnameseDateLabelsAndMonthLabel()
    {
        var sut = CreateSut();
        var result = await sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
        {
            DoctorId = "DOC-001",
        });

        Assert.Equal(7, result.Dates.Count);
        Assert.Equal("Tháng 11, 2026", result.MonthLabel);

        Assert.Equal("2026-11-02", result.Dates[0].DateIso);
        Assert.Equal("Hôm nay", result.Dates[0].DayTopLabel);
        Assert.True(result.Dates[0].IsToday);

        Assert.Equal("2026-11-03", result.Dates[1].DateIso);
        Assert.Equal("Ngày mai", result.Dates[1].SubDateLabel);

        Assert.Equal("2026-11-08", result.Dates[6].DateIso);
        Assert.Equal("Chủ Nhật", result.Dates[6].DayTopLabel);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_OutOfRangeDateIso_FallsBackToFirstAvailableDay()
    {
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 601,
            DoctorId = 1,
            ServiceId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 11, 4, 8, 30, 0),
            EndsAt = new DateTime(2026, 11, 4, 9, 0, 0),
            Capacity = 3,
            Status = "OPEN",
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var result = await sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
        {
            DoctorId = "DOC-001",
            DateIso = "2026-10-13",
        });

        var slot = Assert.Single(result.MorningSlots);
        Assert.Equal("601", slot.Id);
        Assert.Contains("04/11", result.ActiveDayShift.ShiftSummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_InvalidDateIsoFormat_ThrowsValidationException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
            {
                DoctorId = "DOC-001",
                DateIso = "13/10/2026",
            }));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_MorningOnlySlots_SetsAfternoonShiftFalseAndOffShiftNote()
    {
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 801,
            DoctorId = 1,
            ServiceId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 11, 3, 8, 0, 0),
            EndsAt = new DateTime(2026, 11, 3, 8, 30, 0),
            Capacity = 3,
            Status = "OPEN",
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var result = await sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
        {
            DoctorId = "DOC-001",
            DateIso = "2026-11-03",
        });

        Assert.True(result.ActiveDayShift.HasMorningShift);
        Assert.False(result.ActiveDayShift.HasAfternoonShift);
        Assert.NotNull(result.ActiveDayShift.OffShiftNote);
        Assert.Contains("chỉ đặt được Ca Sáng", result.ActiveDayShift.OffShiftNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_InvalidOrUnauthorizedDoctorAndService_ThrowsExpectedException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
            {
                DoctorId = "doc-01",
                ServiceId = "EX-GI-02",
            }));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
            {
                DoctorId = "DOC-999",
            }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
            {
                DoctorId = "   ",
            }));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_PatientProfiles_ReturnsSelfAndVerifiedChildAndExcludesRevoked()
    {
        var sut = CreateSut();
        var result = await sut.GetAvailableSlotsAsync(1, new GetAvailableSlotsRequest
        {
            DoctorId = "DOC-001",
        });

        Assert.Equal(2, result.Profiles.Count);

        var self = result.Profiles[0];
        Assert.Equal("PAT-0001", self.PatientCode);
        Assert.Equal("(Bản thân)", self.RelationshipLabel);
        Assert.Equal("primary", self.BadgeVariant);

        var child = result.Profiles[1];
        Assert.Equal("PAT-0003", child.PatientCode);
        Assert.Equal("(Con)", child.RelationshipLabel);
        Assert.Equal("pediatric", child.BadgeVariant);
        Assert.Equal("Nhi khoa", child.BadgeText);
    }

    [Fact]
    public async Task GetServiceShiftsAsync_ValidService_AggregatesMorningAndAfternoonShifts()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 1101,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 8, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 8, 30, 0),
                Capacity = 4,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 1102,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });
        _db.Appointments.Add(new Appointment { Id = 11, SlotId = 1101, PatientId = 1, Status = "BOOKED" });
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var result = await sut.GetServiceShiftsAsync(1, new GetServiceShiftsRequest
        {
            ServiceId = "EX-GI-01",
            DateIso = "2026-11-03",
        });

        Assert.Equal(2, result.Shifts.Count);

        var morning = result.Shifts[0];
        Assert.Equal("shift-morning", morning.Id);
        Assert.Equal("MORNING", morning.Shift);
        Assert.Equal(6, morning.RemainingCapacity);
        Assert.Equal("AVAILABLE", morning.Status);
        Assert.Equal("Còn 6 lượt khám", morning.BadgeText);

        var afternoon = result.Shifts[1];
        Assert.Equal("shift-afternoon", afternoon.Id);
        Assert.Equal("AFTERNOON", afternoon.Shift);
        Assert.Equal(0, afternoon.RemainingCapacity);
        Assert.Equal("FULL", afternoon.Status);
        Assert.Equal("Không mở lịch", afternoon.BadgeText);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_UserWithoutPatientRecord_ThrowsNotFoundException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetAvailableSlotsAsync(999, new GetAvailableSlotsRequest
            {
                DoctorId = "DOC-001",
            }));
    }
}
