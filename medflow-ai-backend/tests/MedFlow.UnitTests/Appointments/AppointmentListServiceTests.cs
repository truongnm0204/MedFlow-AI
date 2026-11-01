using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentListServiceTests : IDisposable
{
    private sealed class ListTestDbContext : DbContext, IApplicationDbContext
    {
        public ListTestDbContext(DbContextOptions<ListTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();

        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
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
            modelBuilder.Ignore<DoctorServiceAuthorization>();
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
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong AppointmentListServiceTests.");
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

    private readonly ListTestDbContext _db;

    public AppointmentListServiceTests()
    {
        var options = new DbContextOptionsBuilder<ListTestDbContext>()
            .UseInMemoryDatabase($"AppointmentListTests_{Guid.NewGuid():N}")
            .Options;

        _db = new ListTestDbContext(options);
        SeedBaseData(_db);
    }

    public void Dispose() => _db.Dispose();

    private AppointmentListService CreateSut()
        => new(_db, new FixedTimeProvider(DefaultUtcNow));

    private static void SeedBaseData(ListTestDbContext db)
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

        var service = new Service
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
            ServicePrices =
            [
                new ServicePrice
                {
                    Id = 1,
                    ServiceId = 1,
                    PriceAmount = 290_000m,
                    EffectiveFrom = new DateTime(2026, 1, 1),
                }
            ]
        };
        db.Services.Add(service);

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
            Status = "ACTIVE",
        };

        var patientOther = new Patient
        {
            Id = 2,
            PatientCode = "PAT-0002",
            FullName = "Trần Thị Bình",
            DateOfBirth = new DateOnly(1985, 3, 20),
            Gender = "FEMALE",
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
        db.Patients.AddRange(patientSelf, patientOther, patientChild, patientRevoked);

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

        // Slots (quá khứ + tương lai)
        db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 10,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 20, 8, 0, 0),
                EndsAt = new DateTime(2026, 10, 20, 8, 30, 0),
                Capacity = 2,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 11,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 3, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 3, 9, 30, 0),
                Capacity = 2,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 12,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 4, 14, 0, 0),
                EndsAt = new DateTime(2026, 11, 4, 14, 30, 0),
                Capacity = 2,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 13,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 25, 10, 0, 0),
                EndsAt = new DateTime(2026, 10, 25, 10, 30, 0),
                Capacity = 2,
                Status = "OPEN",
            });

        // Appointments:
        // 101: self, past slot 10, CHECKED_IN -> Visit COMPLETED, Payment PAID 290_000
        // 102: child (member 3), future slot 11, BOOKED, QueueNumber = 7
        // 103: self, future slot 12, CONFIRMED
        // 104: self, past slot 13, CANCELLED (no payment)
        // 105: revoked member 4 -> must NOT appear
        // 106: other patient 2 -> must NOT appear
        db.Appointments.AddRange(
            new Appointment
            {
                Id = 101,
                AppointmentCode = "APT-202610-0101",
                PatientId = 1,
                SlotId = 10,
                BookingChannel = "ONLINE",
                Status = "CHECKED_IN",
                BookedAt = new DateTime(2026, 10, 15, 10, 0, 0),
            },
            new Appointment
            {
                Id = 102,
                AppointmentCode = "APT-202611-0102",
                PatientId = 3,
                SlotId = 11,
                BookingChannel = "ONLINE",
                Status = "BOOKED",
                BookedAt = new DateTime(2026, 11, 1, 9, 0, 0),
            },
            new Appointment
            {
                Id = 103,
                AppointmentCode = "APT-202611-0103",
                PatientId = 1,
                SlotId = 12,
                BookingChannel = "COUNTER",
                Status = "CONFIRMED",
                BookedAt = new DateTime(2026, 11, 1, 11, 0, 0),
            },
            new Appointment
            {
                Id = 104,
                AppointmentCode = "APT-202610-0104",
                PatientId = 1,
                SlotId = 13,
                BookingChannel = "ONLINE",
                Status = "CANCELLED",
                BookedAt = new DateTime(2026, 10, 20, 15, 0, 0),
            },
            new Appointment
            {
                Id = 105,
                AppointmentCode = "APT-REVOKED-0105",
                PatientId = 4,
                SlotId = 11,
                BookingChannel = "ONLINE",
                Status = "BOOKED",
                BookedAt = new DateTime(2026, 11, 1, 12, 0, 0),
            },
            new Appointment
            {
                Id = 106,
                AppointmentCode = "APT-OTHER-0106",
                PatientId = 2,
                SlotId = 11,
                BookingChannel = "ONLINE",
                Status = "BOOKED",
                BookedAt = new DateTime(2026, 11, 1, 12, 30, 0),
            });

        db.Visits.Add(new Visit
        {
            Id = 501,
            AppointmentId = 101,
            ReasonForVisit = "Đau thượng vị kéo dài",
            Status = "COMPLETED",
        });

        db.QueueEntries.Add(new QueueEntry
        {
            Id = 601,
            AppointmentId = 102,
            QueueNumber = 7,
            QueueType = "EXAMINATION",
        });

        db.Payments.Add(new Payment
        {
            Id = 701,
            VisitId = 501,
            Amount = 290_000m,
            Status = "PAID",
            PaidAt = new DateTime(2026, 10, 20, 7, 50, 0),
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_ReturnsOnlySelfAndActiveMember_WithCorrectMemberCounts()
    {
        var sut = CreateSut();

        var result = await sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest());

        Assert.Equal(4, result.Total);
        Assert.Equal(4, result.TotalAllMembersCount);
        Assert.DoesNotContain(result.Items, i => i.AppointmentCode == "APT-REVOKED-0105");
        Assert.DoesNotContain(result.Items, i => i.AppointmentCode == "APT-OTHER-0106");

        Assert.Equal(2, result.MemberCounts.Count);
        Assert.Equal("1", result.MemberCounts[0].Id);
        Assert.Equal("PAT-0001", result.MemberCounts[0].PatientCode);
        Assert.Equal("Bản thân", result.MemberCounts[0].RelationshipLabel);
        Assert.Equal("36 tuổi", result.MemberCounts[0].AgeLabel);
        Assert.Equal(3, result.MemberCounts[0].AppointmentCount);

        Assert.Equal("3", result.MemberCounts[1].Id);
        Assert.Equal("PAT-0003", result.MemberCounts[1].PatientCode);
        Assert.Equal("Con", result.MemberCounts[1].RelationshipLabel);
        Assert.Equal("8 tuổi", result.MemberCounts[1].AgeLabel);
        Assert.Equal(1, result.MemberCounts[1].AppointmentCount);
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_MemberOwnershipAndFormatValidation_ThrowsExpectedExceptions()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest { MemberId = "2" }));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest { MemberId = "4" }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest { MemberId = "abc" }));
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_CheckedInWithCompletedVisit_MapsToCompletedAndPreTabCounts()
    {
        var sut = CreateSut();

        var result = await sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest { Tab = "COMPLETED" });

        var item = Assert.Single(result.Items);
        Assert.Equal("APT-202610-0101", item.AppointmentCode);
        Assert.Equal("COMPLETED", item.Status);
        Assert.Equal("ĐÃ HOÀN TẤT KHÁM", item.StatusBannerLabel);
        Assert.Equal("Đau thượng vị kéo dài", item.ReasonForVisit);

        // TabCounts tính trước khi lọc tab
        Assert.Equal(4, result.TabCounts.All);
        Assert.Equal(2, result.TabCounts.Upcoming);
        Assert.Equal(1, result.TabCounts.Completed);
        Assert.Equal(1, result.TabCounts.Cancelled);
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_MapsPaymentStatusesCorrectly()
    {
        var sut = CreateSut();

        var result = await sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest());

        var completed = Assert.Single(result.Items, i => i.AppointmentCode == "APT-202610-0101");
        Assert.Equal("PAID_VIETQR", completed.PaymentStatus);
        Assert.Equal("Đã thanh toán 290.000 VNĐ qua VietQR", completed.PaymentStatusLabel);
        Assert.Null(completed.PaymentSubNote);

        var cancelled = Assert.Single(result.Items, i => i.AppointmentCode == "APT-202610-0104");
        Assert.Equal("REFUNDED_OR_FREE", cancelled.PaymentStatus);
        Assert.Equal("Không phát sinh chi phí", cancelled.PaymentStatusLabel);
        Assert.Null(cancelled.PaymentSubNote);

        var upcoming = Assert.Single(result.Items, i => i.AppointmentCode == "APT-202611-0102");
        Assert.Equal("UNPAID_AT_COUNTER", upcoming.PaymentStatus);
        Assert.Equal("Chờ thanh toán tại quầy", upcoming.PaymentStatusLabel);
        Assert.Equal("Hoặc quét VietQR tự động khi check-in tại viện.", upcoming.PaymentSubNote);
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_UpcomingTab_SortsAscending_Paginates_AndFormatsQueueNumber()
    {
        var sut = CreateSut();

        var fullUpcoming = await sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest
        {
            Tab = "UPCOMING",
        });

        Assert.Equal(2, fullUpcoming.Items.Count);
        Assert.Equal("APT-202611-0102", fullUpcoming.Items[0].AppointmentCode);
        Assert.Equal("007", fullUpcoming.Items[0].QueueNumber);
        Assert.Equal("08:45", fullUpcoming.Items[0].CheckInDeadlineTime);
        Assert.Equal("Ngày mai, 03/11/2026", fullUpcoming.Items[0].ScheduledDateBadge);
        Assert.Equal("APT-202611-0103", fullUpcoming.Items[1].AppointmentCode);

        var page2 = await sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest
        {
            Tab = "UPCOMING",
            PageSize = 1,
            Page = 2,
        });

        Assert.Equal(2, page2.Total);
        Assert.Equal(2, page2.TotalPages);
        Assert.Equal(2, page2.Page);
        var pagedItem = Assert.Single(page2.Items);
        Assert.Equal("APT-202611-0103", pagedItem.AppointmentCode);
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_KeywordWithHashAndDateRange_FiltersCorrectly()
    {
        var sut = CreateSut();

        var result = await sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest
        {
            Keyword = "  #APT-202611-0102 ",
            DateFrom = "2026-11-01",
            DateTo = "2026-11-05",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("APT-202611-0102", item.AppointmentCode);
        Assert.Equal(1, result.TabCounts.All);
        // MemberCounts vẫn phản ánh tổng toàn bộ lịch của gia đình
        Assert.Equal(4, result.TotalAllMembersCount);
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_InvalidFilterInputs_ThrowsValidationException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest
            {
                DateFrom = "2026-11-10",
                DateTo = "2026-11-01",
            }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest
            {
                PageSize = 51,
            }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest
            {
                Keyword = new string('a', 101),
            }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentsAsync(1, new GetMyAppointmentsRequest
            {
                Tab = "FOO",
            }));
    }
}
