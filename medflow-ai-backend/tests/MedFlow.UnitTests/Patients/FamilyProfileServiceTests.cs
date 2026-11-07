using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Patients;

public sealed class FamilyProfileServiceTests : IDisposable
{
    private sealed class FamilyTestDbContext : DbContext, IApplicationDbContext
    {
        public FamilyTestDbContext(DbContextOptions<FamilyTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<Service> Services => Set<Service>();

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Staff>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Hospital>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

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
                b.HasOne(l => l.OwnerPatient)
                    .WithMany()
                    .HasForeignKey(l => l.OwnerPatientId);
                b.HasOne(l => l.MemberPatient)
                    .WithMany()
                    .HasForeignKey(l => l.MemberPatientId);
            });
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.Department);
                b.Ignore(s => s.Specialty);
                b.Ignore(s => s.ServicePrices);
            });
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.Doctor);
                b.Ignore(s => s.Room);
                b.Ignore(s => s.Appointments);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
            });
            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.HasOne(a => a.Slot).WithMany().HasForeignKey(a => a.SlotId);
                b.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId);
            });
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong FamilyProfileServiceTests.");
    }

    private readonly FamilyTestDbContext _db;
    private readonly FamilyProfileService _sut;

    public FamilyProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<FamilyTestDbContext>()
            .UseInMemoryDatabase($"family-profile-{Guid.NewGuid():N}")
            .Options;

        _db = new FamilyTestDbContext(options);
        _sut = new FamilyProfileService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetFamilyMembers_ValidOwner_ReturnsPrimaryAndLinkedMembers()
    {
        // Arrange
        var user = new User { Id = 1, Email = "owner@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient
        {
            Id = 10,
            UserId = user.Id,
            User = user,
            PatientCode = "PAT-0010",
            FullName = "Nguyễn Văn An",
            Gender = "MALE",
            DateOfBirth = new DateOnly(1985, 5, 20),
            Phone = "0901234567",
            IdentificationHash = "some-hash",
            Status = "ACTIVE",
            Address = "Hà Nội",
            BloodType = "O+"
        };

        var memberChild = new Patient
        {
            Id = 20,
            PatientCode = "PAT-0020",
            FullName = "Nguyễn Minh Khang",
            Gender = "MALE",
            DateOfBirth = new DateOnly(2018, 4, 12),
            Status = "ACTIVE",
            BloodType = "A+"
        };

        var memberSpouse = new Patient
        {
            Id = 30,
            PatientCode = "PAT-0030",
            FullName = "Nguyễn Thị Bình",
            Gender = "FEMALE",
            DateOfBirth = new DateOnly(1988, 8, 15),
            Status = "ACTIVE",
            BloodType = "B+"
        };

        var linkChild = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = memberChild.Id,
            MemberPatient = memberChild,
            Relationship = "CHILD",
            VerificationStatus = "VERIFIED",
            VerifiedAt = DateTime.UtcNow
        };

        var linkSpouse = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = memberSpouse.Id,
            MemberPatient = memberSpouse,
            Relationship = "SPOUSE",
            VerificationStatus = "VERIFIED",
            VerifiedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, memberChild, memberSpouse);
        _db.PatientFamilyLinks.AddRange(linkChild, linkSpouse);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMembersAsync(user.Id, includeSelf: true);

        // Assert
        Assert.Equal(3, result.Count);

        // Item 0: Primary account (Self)
        var self = result[0];
        Assert.True(self.IsPrimaryAccount);
        Assert.Equal("Bản thân", self.Relationship);
        Assert.Equal("slate", self.RelationshipBadgeColor);
        Assert.Equal("Nguyễn Văn An", self.FullName);
        Assert.Equal("PAT-0010", self.PatientCode);
        Assert.Equal("Nam", self.Gender);
        Assert.True(self.IsIdentityVerified);
        Assert.Equal("****** (Đã xác thực CCCD)", self.IdentificationNo);
        Assert.Equal("Ủy quyền toàn phần", self.SubStatus);
        Assert.NotNull(self.Permissions);
        Assert.True(self.Permissions.CanManage);
        Assert.True(self.Permissions.CanBookAppointments);
        Assert.True(self.Permissions.CanViewMedicalRecords);

        // Item 1: Child
        var child = result[1];
        Assert.False(child.IsPrimaryAccount);
        Assert.Equal("Con trai", child.Relationship);
        Assert.Equal("blue", child.RelationshipBadgeColor);
        Assert.Equal("Nguyễn Minh Khang", child.FullName);
        Assert.Equal("Hồ sơ nhi khoa ngoại trú", child.SubStatus);
        Assert.Equal("Nguyễn Văn An", child.Guardian);
        Assert.Equal("A+", child.Vitals?.BloodType);

        // Item 2: Spouse
        var spouse = result[2];
        Assert.False(spouse.IsPrimaryAccount);
        Assert.Equal("Vợ", spouse.Relationship);
        Assert.Equal("rose", spouse.RelationshipBadgeColor);
        Assert.Equal("Nguyễn Thị Bình", spouse.FullName);
        Assert.Equal("Hồ sơ liên kết gia đình", spouse.SubStatus);
        Assert.Equal("B+", spouse.Vitals?.BloodType);
    }

    [Fact]
    public async Task GetFamilyMembers_IncludeSelfFalse_ReturnsOnlyLinkedMembers()
    {
        // Arrange
        var user = new User { Id = 2, Email = "owner2@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 11, UserId = user.Id, User = user, Status = "ACTIVE", FullName = "Owner" };
        var member = new Patient { Id = 21, FullName = "Member", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            MemberPatient = member,
            Relationship = "PARENT",
            VerificationStatus = "VERIFIED"
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMembersAsync(user.Id, includeSelf: false);

        // Assert
        Assert.Single(result);
        Assert.False(result[0].IsPrimaryAccount);
        Assert.Equal("Member", result[0].FullName);
    }

    [Fact]
    public async Task GetFamilyMembers_NoLinkedMembers_ReturnsOnlyPrimaryMember()
    {
        // Arrange
        var user = new User { Id = 3, Email = "owner3@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 12, UserId = user.Id, User = user, Status = "ACTIVE", FullName = "Solo Owner" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMembersAsync(user.Id, includeSelf: true);

        // Assert
        Assert.Single(result);
        Assert.True(result[0].IsPrimaryAccount);
        Assert.Equal("Solo Owner", result[0].FullName);
    }

    [Fact]
    public async Task GetFamilyMembers_InvalidUserId_ThrowsUnauthorizedAccessException()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetFamilyMembersAsync(0));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetFamilyMembersAsync(-1));
    }

    [Fact]
    public async Task GetFamilyMembers_PatientNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetFamilyMembersAsync(9999));
    }

    [Fact]
    public async Task GetFamilyMembers_UserInactive_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var user = new User { Id = 4, Email = "suspended@medflow.test", AccountStatus = AccountStatus.LOCKED };
        var owner = new Patient { Id = 13, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetFamilyMembersAsync(user.Id));
    }

    [Fact]
    public async Task GetFamilyMembers_PatientInactive_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var user = new User { Id = 5, Email = "active@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 14, UserId = user.Id, User = user, Status = "INACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetFamilyMembersAsync(user.Id));
    }

    [Fact]
    public async Task GetFamilyMembers_RevokedLink_IsExcludedFromList()
    {
        // Arrange
        var user = new User { Id = 6, Email = "revoked_test@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 15, UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 22, FullName = "Revoked Member", Status = "ACTIVE" };
        var revokedLink = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            MemberPatient = member,
            Relationship = "CHILD",
            RevokedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(revokedLink);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMembersAsync(user.Id, includeSelf: false);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFamilyMembers_InactiveMemberPatient_IsExcludedFromList()
    {
        // Arrange
        var user = new User { Id = 7, Email = "inactive_member_test@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 16, UserId = user.Id, User = user, Status = "ACTIVE" };
        var inactiveMember = new Patient { Id = 23, FullName = "Inactive Member", Status = "INACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = inactiveMember.Id,
            MemberPatient = inactiveMember,
            Relationship = "CHILD",
            RevokedAt = null
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, inactiveMember);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMembersAsync(user.Id, includeSelf: false);

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("CHILD", "FEMALE", "Con gái", "blue")]
    [InlineData("CHILD", "MALE", "Con trai", "blue")]
    [InlineData("SPOUSE", "FEMALE", "Vợ", "rose")]
    [InlineData("SPOUSE", "MALE", "Chồng", "rose")]
    [InlineData("PARENT", "FEMALE", "Mẹ ruột", "amber")]
    [InlineData("PARENT", "MALE", "Bố ruột", "amber")]
    [InlineData("OTHER", null, "Khác", "slate")]
    [InlineData("Mẹ ruột", "FEMALE", "Mẹ ruột", "amber")]
    public async Task GetFamilyMembers_RelationshipMapping_MapsEnglishAndGenderCorrectly(
        string rawRelationship,
        string? memberGender,
        string expectedRelationship,
        string expectedBadgeColor)
    {
        // Arrange
        var user = new User { Id = 100 + Random.Shared.Next(1, 10000), Email = $"rel_{Guid.NewGuid():N}@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 200 + Random.Shared.Next(1, 10000), UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 300 + Random.Shared.Next(1, 10000), FullName = "Test Member", Gender = memberGender, Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            MemberPatient = member,
            Relationship = rawRelationship,
            RevokedAt = null
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMembersAsync(user.Id, includeSelf: false);

        // Assert
        Assert.Single(result);
        Assert.Equal(expectedRelationship, result[0].Relationship);
        Assert.Equal(expectedBadgeColor, result[0].RelationshipBadgeColor);
    }

    // ==================== TESTS FOR GetFamilyMemberDetailAsync (M1-030) ====================

    [Fact]
    public async Task GetFamilyMemberDetail_ValidLinkedMemberById_ReturnsCorrectDetail()
    {
        // Arrange
        var user = new User { Id = 401, Email = "detail_owner@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient
        {
            Id = 410,
            UserId = user.Id,
            User = user,
            PatientCode = "PAT-0410",
            FullName = "Nguyễn Văn Cha",
            Status = "ACTIVE",
            Phone = "0909999888",
            Address = "Hà Nội"
        };
        var child = new Patient
        {
            Id = 420,
            PatientCode = "PAT-0420",
            FullName = "Nguyễn Văn Con",
            Gender = "MALE",
            DateOfBirth = new DateOnly(2015, 6, 1),
            Status = "ACTIVE",
            BloodType = "AB+",
            IdentificationHash = "child-hash"
        };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = child.Id,
            MemberPatient = child,
            Relationship = "CHILD",
            VerificationStatus = "VERIFIED",
            VerifiedAt = DateTime.UtcNow
        };

        var visit = new Visit { Id = 1001, PatientId = child.Id, Status = "COMPLETED" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, child);
        _db.PatientFamilyLinks.Add(link);
        _db.Visits.Add(visit);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMemberDetailAsync(user.Id, "420");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("FAM-420", result.Id);
        Assert.Equal(420, result.MemberPatientId);
        Assert.Equal("Nguyễn Văn Con", result.FullName);
        Assert.Equal("Con trai", result.Relationship);
        Assert.Equal("blue", result.RelationshipBadgeColor);
        Assert.Equal("Nguyễn Văn Cha", result.Guardian);
        Assert.Equal("AB+", result.Vitals?.BloodType);
        Assert.Equal(1, result.OutpatientVisitsCount);
        Assert.True(result.IsIdentityVerified);
        Assert.NotNull(result.Permissions);
        Assert.True(result.Permissions.CanManage);
        Assert.True(result.Permissions.CanBookAppointments);
    }

    [Fact]
    public async Task GetFamilyMemberDetail_ValidLinkedMemberByFamCode_ReturnsCorrectDetail()
    {
        // Arrange
        var user = new User { Id = 402, Email = "fam_code_test@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 411, UserId = user.Id, User = user, Status = "ACTIVE", FullName = "Owner" };
        var member = new Patient { Id = 421, PatientCode = "PAT-0421", FullName = "Spouse Member", Gender = "FEMALE", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            MemberPatient = member,
            Relationship = "SPOUSE",
            VerificationStatus = "VERIFIED"
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMemberDetailAsync(user.Id, "FAM-421");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Spouse Member", result.FullName);
        Assert.Equal("Vợ", result.Relationship);
        Assert.Equal("rose", result.RelationshipBadgeColor);
    }

    [Fact]
    public async Task GetFamilyMemberDetail_ValidLinkedMemberByPatientCode_ReturnsCorrectDetail()
    {
        // Arrange
        var user = new User { Id = 403, Email = "pat_code_test@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 412, UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 422, PatientCode = "PAT-2026-9042", FullName = "Khang", Gender = "MALE", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            MemberPatient = member,
            Relationship = "CHILD",
            VerificationStatus = "VERIFIED"
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // Act & Assert with '#PAT-2026-9042'
        var result = await _sut.GetFamilyMemberDetailAsync(user.Id, "#PAT-2026-9042");
        Assert.NotNull(result);
        Assert.Equal("Khang", result.FullName);

        // Act & Assert without '#'
        var result2 = await _sut.GetFamilyMemberDetailAsync(user.Id, "PAT-2026-9042");
        Assert.NotNull(result2);
        Assert.Equal("Khang", result2.FullName);
    }

    [Fact]
    public async Task GetFamilyMemberDetail_SelfIdentifier_ReturnsPrimaryAccountDetail()
    {
        // Arrange
        var user = new User { Id = 404, Email = "self_test@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 413, UserId = user.Id, User = user, FullName = "Primary An", Status = "ACTIVE", PatientCode = "PAT-0413" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        // Act
        var resultMe = await _sut.GetFamilyMemberDetailAsync(user.Id, "me");
        var resultSelf = await _sut.GetFamilyMemberDetailAsync(user.Id, "self");

        // Assert
        Assert.True(resultMe.IsPrimaryAccount);
        Assert.Equal("Primary An", resultMe.FullName);
        Assert.Equal("Bản thân", resultMe.Relationship);

        Assert.True(resultSelf.IsPrimaryAccount);
        Assert.Equal("Primary An", resultSelf.FullName);
    }

    [Fact]
    public async Task GetFamilyMemberDetail_NeedsReviewLink_ReturnsReadOnlySubStatusAndCanManageFalse()
    {
        // Arrange
        var user = new User { Id = 405, Email = "readonly_test@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 414, UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 424, FullName = "Unverified Mai", Gender = "FEMALE", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            MemberPatient = member,
            Relationship = "PARENT",
            VerificationStatus = "NEEDS_REVIEW"
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetFamilyMemberDetailAsync(user.Id, "424");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Chỉ đọc", result.SubStatus);
        Assert.NotNull(result.Permissions);
        Assert.False(result.Permissions.CanManage);
        Assert.Equal("NEEDS_REVIEW", result.Permissions.RelationshipStatus);
    }

    [Fact]
    public async Task GetFamilyMemberDetail_UnlinkedPatient_ThrowsNotFoundException()
    {
        // Arrange - Patient 425 exists in DB but is NOT linked to owner 415
        var user = new User { Id = 406, Email = "unlinked_test@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 415, UserId = user.Id, User = user, Status = "ACTIVE" };
        var stranger = new Patient { Id = 425, FullName = "Stranger Patient", Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, stranger);
        await _db.SaveChangesAsync();

        // Act & Assert (Layer 2 ownership enforcement!)
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetFamilyMemberDetailAsync(user.Id, "425"));
    }

    [Fact]
    public async Task GetFamilyMemberDetail_RevokedLink_ThrowsNotFoundException()
    {
        // Arrange
        var user = new User { Id = 407, Email = "revoked_detail_test@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 416, UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 426, FullName = "Revoked Detail", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            MemberPatient = member,
            Relationship = "CHILD",
            RevokedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetFamilyMemberDetailAsync(user.Id, "426"));
    }

    [Fact]
    public async Task GetFamilyMemberDetail_InactiveMemberPatient_ThrowsNotFoundException()
    {
        // Arrange
        var user = new User { Id = 408, Email = "inactive_patient_test@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 417, UserId = user.Id, User = user, Status = "ACTIVE" };
        var inactiveMember = new Patient { Id = 427, FullName = "Inactive Patient", Status = "INACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = inactiveMember.Id,
            MemberPatient = inactiveMember,
            Relationship = "CHILD",
            RevokedAt = null
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, inactiveMember);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetFamilyMemberDetailAsync(user.Id, "427"));
    }

    [Fact]
    public async Task GetFamilyMemberDetail_NonExistentIdentifier_ThrowsNotFoundException()
    {
        var user = new User { Id = 409, Email = "nonexistent_test@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 418, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetFamilyMemberDetailAsync(user.Id, "FAM-9999"));
    }

    [Fact]
    public async Task GetFamilyMemberDetail_EmptyIdentifier_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetFamilyMemberDetailAsync(1, ""));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetFamilyMemberDetailAsync(1, "   "));
    }

    [Fact]
    public async Task GetFamilyMemberDetail_InvalidUserId_ThrowsUnauthorizedAccessException()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetFamilyMemberDetailAsync(0, "FAM-001"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetFamilyMemberDetailAsync(-10, "FAM-001"));
    }

    [Fact]
    public async Task GetFamilyMemberDetail_OwnerNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetFamilyMemberDetailAsync(99999, "FAM-001"));
    }

    [Fact]
    public async Task GetFamilyMemberDetail_OwnerInactive_ThrowsUnauthorizedAccessException()
    {
        var user = new User { Id = 450, Email = "inactive_owner@test.com", AccountStatus = AccountStatus.LOCKED };
        var owner = new Patient { Id = 460, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetFamilyMemberDetailAsync(user.Id, "FAM-001"));
    }

    // ── CreateFamilyMemberAsync Tests ──────────────────────────────────────────

    [Fact]
    public async Task CreateFamilyMember_ValidNewChild_ReturnsCreatedMemberWithCorrectDetails()
    {
        var user = new User { Id = 501, Email = "create_child_user@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 511, UserId = user.Id, User = user, FullName = "Bố Chủ Hộ", Phone = "0901234567", Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Bé Con",
            Relationship = "Con gái",
            Gender = "Nữ",
            BirthDate = "2018-06-15",
            Address = "Số 123 Đường Láng, Hà Nội",
            Phone = "0987654321",
            Email = "becon@test.com"
        };

        var result = await _sut.CreateFamilyMemberAsync(user.Id, request);

        Assert.NotNull(result);
        Assert.Equal("Nguyễn Bé Con", result.FullName);
        Assert.Equal("Con gái", result.Relationship);
        Assert.Equal("blue", result.RelationshipBadgeColor);
        Assert.Equal("Nữ", result.Gender);
        Assert.Equal("15/06/2018", result.BirthDate);
        Assert.False(result.IsPrimaryAccount);
        Assert.StartsWith("PAT-", result.PatientCode);
        Assert.NotNull(result.Permissions);
        Assert.True(result.Permissions.CanManage);
        Assert.True(result.Permissions.CanBookAppointments);
        Assert.Equal("Hồ sơ nhi khoa ngoại trú", result.SubStatus);
        Assert.Equal("Bố Chủ Hộ", result.Guardian);

        // Verify persisted in DB
        var savedPatient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == result.MemberPatientId);
        Assert.NotNull(savedPatient);
        Assert.Equal("Nguyễn Bé Con", savedPatient.FullName);
        Assert.Equal("FEMALE", savedPatient.Gender);

        var savedLink = await _db.PatientFamilyLinks
            .FirstOrDefaultAsync(l => l.OwnerPatientId == owner.Id && l.MemberPatientId == savedPatient.Id);
        Assert.NotNull(savedLink);
        Assert.Equal("CHILD", savedLink.Relationship);
        Assert.Null(savedLink.RevokedAt);
    }

    [Fact]
    public async Task CreateFamilyMember_ValidNewParent_WithCCCD_ReturnsVerifiedMember()
    {
        var user = new User { Id = 502, Email = "create_parent_user@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 512, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Trần Thị Mẹ",
            Relationship = "Mẹ ruột",
            Gender = "Nữ",
            BirthDate = "1960-03-20",
            Address = "Số 456 Giải Phóng, Hà Nội",
            IdentificationNo = "001160123456",
            BloodType = "O+"
        };

        var result = await _sut.CreateFamilyMemberAsync(user.Id, request);

        Assert.NotNull(result);
        Assert.Equal("Trần Thị Mẹ", result.FullName);
        Assert.Equal("Mẹ ruột", result.Relationship);
        Assert.Equal("amber", result.RelationshipBadgeColor);
        Assert.True(result.IsIdentityVerified);
        Assert.Contains("Đã xác thực CCCD", result.IdentificationNo);
        Assert.Equal("Hồ sơ người cao tuổi", result.SubStatus);
        Assert.Equal("O+", result.Vitals?.BloodType);

        var link = await _db.PatientFamilyLinks.FirstAsync(l => l.OwnerPatientId == owner.Id && l.MemberPatientId == result.MemberPatientId);
        Assert.Equal("VERIFIED", link.VerificationStatus);
        Assert.Equal("PARENT", link.Relationship);
    }

    [Fact]
    public async Task CreateFamilyMember_ExistingPatientInSystem_LinksExistingPatient()
    {
        var user = new User { Id = 503, Email = "link_existing_user@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 513, UserId = user.Id, User = user, Status = "ACTIVE" };

        // An existing patient in DB who is not yet linked to owner
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var cccd = "001188000999";
        var cccdHash = Convert.ToHexString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cccd))).ToLowerInvariant();

        var existingMember = new Patient
        {
            Id = 555,
            PatientCode = "PAT-0555",
            FullName = "Đặng Văn Chồng",
            DateOfBirth = new DateOnly(1985, 1, 1),
            Gender = "MALE",
            Address = "Hà Nội",
            IdentificationHash = cccdHash,
            Status = "ACTIVE"
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, existingMember);
        await _db.SaveChangesAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Đặng Văn Chồng",
            Relationship = "Chồng",
            Gender = "Nam",
            BirthDate = "1985-01-01",
            Address = "Hà Nội",
            IdentificationNo = cccd
        };

        var result = await _sut.CreateFamilyMemberAsync(user.Id, request);

        Assert.NotNull(result);
        Assert.Equal(555, result.MemberPatientId);
        Assert.Equal("PAT-0555", result.PatientCode);
        Assert.Equal("Chồng", result.Relationship);
        Assert.Equal("rose", result.RelationshipBadgeColor);

        var link = await _db.PatientFamilyLinks
            .FirstOrDefaultAsync(l => l.OwnerPatientId == owner.Id && l.MemberPatientId == 555);
        Assert.NotNull(link);
        Assert.Equal("SPOUSE", link.Relationship);
    }

    [Fact]
    public async Task CreateFamilyMember_ExistingPatientAlreadyLinked_ThrowsConflictException()
    {
        var user = new User { Id = 504, Email = "conflict_user@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 514, UserId = user.Id, User = user, Status = "ACTIVE" };

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var cccd = "001188111222";
        var cccdHash = Convert.ToHexString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cccd))).ToLowerInvariant();

        var member = new Patient { Id = 556, PatientCode = "PAT-0556", FullName = "Đã Liên Kết", IdentificationHash = cccdHash, Status = "ACTIVE" };
        var activeLink = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            VerificationStatus = "VERIFIED",
            RevokedAt = null
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(activeLink);
        await _db.SaveChangesAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Đã Liên Kết",
            Relationship = "Con gái",
            Gender = "Nữ",
            BirthDate = "2010-01-01",
            Address = "Hà Nội",
            IdentificationNo = cccd
        };

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.CreateFamilyMemberAsync(user.Id, request));
    }

    [Fact]
    public async Task CreateFamilyMember_ExistingPatientRevokedLink_ReactivatesLink()
    {
        var user = new User { Id = 505, Email = "reactivate_user@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 515, UserId = user.Id, User = user, Status = "ACTIVE" };

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var cccd = "001188333444";
        var cccdHash = Convert.ToHexString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cccd))).ToLowerInvariant();

        var member = new Patient { Id = 557, PatientCode = "PAT-0557", FullName = "Cũ Đã Thu Hồi", Gender = "FEMALE", IdentificationHash = cccdHash, Status = "ACTIVE" };
        var revokedLink = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "OTHER",
            VerificationStatus = "REVOKED",
            RevokedAt = DateTime.UtcNow.AddDays(-10)
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(revokedLink);
        await _db.SaveChangesAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Cũ Đã Thu Hồi",
            Relationship = "Vợ",
            Gender = "Nữ",
            BirthDate = "1990-02-02",
            Address = "Hà Nội",
            IdentificationNo = cccd
        };

        var result = await _sut.CreateFamilyMemberAsync(user.Id, request);

        Assert.NotNull(result);
        Assert.Equal("Vợ", result.Relationship);

        var updatedLink = await _db.PatientFamilyLinks.FirstAsync(l => l.OwnerPatientId == owner.Id && l.MemberPatientId == member.Id);
        Assert.Null(updatedLink.RevokedAt);
        Assert.Equal("SPOUSE", updatedLink.Relationship);
        Assert.Equal("VERIFIED", updatedLink.VerificationStatus);
    }

    [Fact]
    public async Task CreateFamilyMember_CannotAddSelf_ThrowsValidationException()
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var cccd = "001199999999";
        var cccdHash = Convert.ToHexString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cccd))).ToLowerInvariant();

        var user = new User { Id = 506, Email = "self_cccd@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 516, UserId = user.Id, User = user, IdentificationHash = cccdHash, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Chính Tôi",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "1990-01-01",
            Address = "Hà Nội",
            IdentificationNo = cccd
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(user.Id, request));
    }

    [Theory]
    [InlineData("Bản thân")]
    [InlineData("SELF")]
    [InlineData("self")]
    public async Task CreateFamilyMember_RelationshipSelf_ThrowsValidationException(string rel)
    {
        var user = new User { Id = 507, Email = "self_rel@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 517, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = rel,
            Gender = "Nam",
            BirthDate = "1995-01-01",
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(user.Id, request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public async Task CreateFamilyMember_MissingOrShortFullName_ThrowsValidationException(string name)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = name,
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Fact]
    public async Task CreateFamilyMember_FullNameTooLong_ThrowsValidationException()
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = new string('A', 101),
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateFamilyMember_MissingRelationship_ThrowsValidationException(string rel)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = rel,
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("UnknownGender")]
    public async Task CreateFamilyMember_MissingOrInvalidGender_ThrowsValidationException(string gender)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = gender,
            BirthDate = "2020-01-01",
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Fact]
    public async Task CreateFamilyMember_FutureBirthDate_ThrowsValidationException()
    {
        var futureDate = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd");
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Bé Tương Lai",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = futureDate,
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("10/08/201")] // 3-digit year
    [InlineData("201-08-10")] // 3-digit year
    [InlineData("not-a-date")]
    [InlineData("31/02/2020")] // Invalid calendar date
    public async Task CreateFamilyMember_InvalidBirthDateFormatOrDigits_ThrowsValidationException(string birthDate)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Bé Sai Ngày",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = birthDate,
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Fact]
    public async Task CreateFamilyMember_BirthYearBefore1900_ThrowsValidationException()
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Cụ Quá Xưa",
            Relationship = "Bố ruột",
            Gender = "Nam",
            BirthDate = "15/08/1880",
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("Nguyen 123")]
    [InlineData("Bé @#$%")]
    public async Task CreateFamilyMember_FullNameWithDigitsOrSpecialChars_ThrowsValidationException(string name)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = name,
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    public async Task CreateFamilyMember_ShortAddress_ThrowsValidationException(string address)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = address
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("012345678")]
    [InlineData("abcdefghij")]
    public async Task CreateFamilyMember_InvalidPhone_ThrowsValidationException(string phone)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội số 123",
            Phone = phone
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("missing@domain")]
    public async Task CreateFamilyMember_InvalidEmail_ThrowsValidationException(string email)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội số 123",
            Email = email
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("12345678901")]
    [InlineData("not-a-number")]
    public async Task CreateFamilyMember_InvalidCCCD_ThrowsValidationException(string cccd)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội số 123",
            IdentificationNo = cccd
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Theory]
    [InlineData("X+")]
    [InlineData("C-")]
    public async Task CreateFamilyMember_InvalidBloodType_ThrowsValidationException(string bt)
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội số 123",
            BloodType = bt
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateFamilyMemberAsync(1, request));
    }

    [Fact]
    public async Task CreateFamilyMember_InvalidUserId_ThrowsUnauthorizedAccessException()
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội số 123"
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.CreateFamilyMemberAsync(0, request));
    }

    [Fact]
    public async Task CreateFamilyMember_OwnerNotFound_ThrowsNotFoundException()
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội số 123"
        };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateFamilyMemberAsync(99999, request));
    }

    [Fact]
    public async Task CreateFamilyMember_OwnerLocked_ThrowsUnauthorizedAccessException()
    {
        var user = new User { Id = 508, Email = "locked_creator@test.com", AccountStatus = AccountStatus.LOCKED };
        var owner = new Patient { Id = 518, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn Test",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2020-01-01",
            Address = "Hà Nội số 123"
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.CreateFamilyMemberAsync(user.Id, request));
    }

    [Fact]
    public async Task UpdateFamilyMember_ValidContactUpdate_ReturnsUpdatedMember()
    {
        var user = new User { Id = 601, Email = "updater@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 611, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-611" };
        var child = new Patient
        {
            Id = 612,
            FullName = "Nguyễn Minh Khang",
            PatientCode = "PAT-2026-612",
            Gender = "MALE",
            DateOfBirth = new DateOnly(2018, 8, 10),
            Status = "ACTIVE",
            Phone = "0901111111",
            Email = "old@test.com",
            Address = "Số 1 Cũ, Hà Nội"
        };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = child.Id,
            Relationship = "CHILD",
            VerificationStatus = "ACTIVE"
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, child);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest
        {
            Phone = "0912 345 678",
            Email = "new_child@medflow.vn",
            Address = "Số 45 Tràng Tiền, Quận Hoàn Kiếm, Hà Nội",
            EmergencyContactName = "Nguyễn Văn An",
            EmergencyPhone = "0988 765 432"
        };

        var result = await _sut.UpdateFamilyMemberAsync(user.Id, "FAM-612", request);

        Assert.NotNull(result);
        Assert.Equal("Nguyễn Minh Khang", result.FullName);
        Assert.Equal("0912345678", result.Phone);
        Assert.Equal("new_child@medflow.vn", result.Email);
        Assert.Equal("Số 45 Tràng Tiền, Quận Hoàn Kiếm, Hà Nội", result.Address);

        var updatedChild = await _db.Patients.FindAsync(child.Id);
        Assert.NotNull(updatedChild);
        Assert.Equal("0912345678", updatedChild.Phone);
        Assert.Equal("new_child@medflow.vn", updatedChild.Email);
        Assert.Equal("Số 45 Tràng Tiền, Quận Hoàn Kiếm, Hà Nội", updatedChild.Address);
    }

    [Fact]
    public async Task UpdateFamilyMember_ValidRelationshipAndGenderUpdate_UpdatesLinkAndGender()
    {
        var user = new User { Id = 602, Email = "updater2@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 621, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-621" };
        var member = new Patient
        {
            Id = 622,
            FullName = "Nguyễn Bảo Châu",
            PatientCode = "PAT-2026-622",
            Gender = "MALE",
            DateOfBirth = new DateOnly(2020, 5, 20),
            Status = "ACTIVE",
            Address = "Hà Nội số 123"
        };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            VerificationStatus = "ACTIVE"
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest
        {
            Relationship = "Con gái",
            Gender = "Nữ"
        };

        var result = await _sut.UpdateFamilyMemberAsync(user.Id, member.Id.ToString(), request);

        Assert.NotNull(result);
        Assert.Equal("Con gái", result.Relationship);
        Assert.Equal("Nữ", result.Gender);
        Assert.Equal("blue", result.RelationshipBadgeColor);

        var updatedMember = await _db.Patients.FindAsync(member.Id);
        Assert.Equal("FEMALE", updatedMember!.Gender);
    }

    [Fact]
    public async Task UpdateFamilyMember_ValidBirthDateUpdate_UpdatesDateOfBirth()
    {
        var user = new User { Id = 603, Email = "updater3@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 631, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-631" };
        var member = new Patient
        {
            Id = 632,
            FullName = "Nguyễn Văn Bé",
            PatientCode = "PAT-2026-632",
            Gender = "MALE",
            DateOfBirth = new DateOnly(2015, 1, 1),
            Status = "ACTIVE",
            Address = "Hà Nội số 123"
        };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            VerificationStatus = "ACTIVE"
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest
        {
            BirthDate = "15/08/2019"
        };

        var result = await _sut.UpdateFamilyMemberAsync(user.Id, member.PatientCode, request);

        Assert.NotNull(result);
        Assert.Equal("15/08/2019", result.BirthDate);

        var updatedMember = await _db.Patients.FindAsync(member.Id);
        Assert.Equal(new DateOnly(2019, 8, 15), updatedMember!.DateOfBirth);
    }

    [Fact]
    public async Task UpdateFamilyMember_CannotUpdateSelf_ThrowsValidationException()
    {
        var user = new User { Id = 604, Email = "updater4@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 641, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-641" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { Phone = "0912345678" };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, "self", request));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, owner.Id.ToString(), request));
    }

    [Fact]
    public async Task UpdateFamilyMember_RelationshipSelf_ThrowsValidationException()
    {
        var user = new User { Id = 605, Email = "updater5@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 651, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-651" };
        var member = new Patient { Id = 652, FullName = "Nguyễn A", PatientCode = "PAT-2026-652", Status = "ACTIVE", Address = "Hà Nội" };
        var link = new PatientFamilyLink { OwnerPatientId = owner.Id, MemberPatientId = member.Id, Relationship = "CHILD" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { Relationship = "Bản thân" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, member.Id.ToString(), request));

        Assert.Contains("Bản thân", ex.Message);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abcd")]
    public async Task UpdateFamilyMember_ShortAddress_ThrowsValidationException(string address)
    {
        var user = new User { Id = 606, Email = "updater6@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 661, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-661" };
        var member = new Patient { Id = 662, FullName = "Nguyễn A", PatientCode = "PAT-2026-662", Status = "ACTIVE", Address = "Hà Nội" };
        var link = new PatientFamilyLink { OwnerPatientId = owner.Id, MemberPatientId = member.Id, Relationship = "CHILD" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { Address = address };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, member.Id.ToString(), request));

        Assert.Contains("5 ký tự", ex.Message);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("0912abc")]
    [InlineData("1912345678")]
    public async Task UpdateFamilyMember_InvalidPhone_ThrowsValidationException(string phone)
    {
        var user = new User { Id = 607, Email = "updater7@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 671, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-671" };
        var member = new Patient { Id = 672, FullName = "Nguyễn A", PatientCode = "PAT-2026-672", Status = "ACTIVE", Address = "Hà Nội" };
        var link = new PatientFamilyLink { OwnerPatientId = owner.Id, MemberPatientId = member.Id, Relationship = "CHILD" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { Phone = phone };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, member.Id.ToString(), request));
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@missingusername.com")]
    public async Task UpdateFamilyMember_InvalidEmail_ThrowsValidationException(string email)
    {
        var user = new User { Id = 608, Email = "updater8@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 681, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-681" };
        var member = new Patient { Id = 682, FullName = "Nguyễn A", PatientCode = "PAT-2026-682", Status = "ACTIVE", Address = "Hà Nội" };
        var link = new PatientFamilyLink { OwnerPatientId = owner.Id, MemberPatientId = member.Id, Relationship = "CHILD" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { Email = email };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, member.Id.ToString(), request));
    }

    [Theory]
    [InlineData("10/08/201")]
    [InlineData("201-08-10")]
    [InlineData("31/02/2020")]
    [InlineData("2099-01-01")]
    public async Task UpdateFamilyMember_InvalidBirthDate_ThrowsValidationException(string birthDate)
    {
        var user = new User { Id = 609, Email = "updater9@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 691, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-691" };
        var member = new Patient { Id = 692, FullName = "Nguyễn A", PatientCode = "PAT-2026-692", Status = "ACTIVE", Address = "Hà Nội" };
        var link = new PatientFamilyLink { OwnerPatientId = owner.Id, MemberPatientId = member.Id, Relationship = "CHILD" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { BirthDate = birthDate };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, member.Id.ToString(), request));
    }

    [Theory]
    [InlineData("Nguyễn 123")]
    [InlineData("Bé @#$%")]
    public async Task UpdateFamilyMember_FullNameWithDigitsOrSymbols_ThrowsValidationException(string name)
    {
        var user = new User { Id = 610, Email = "updater10@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 701, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-701" };
        var member = new Patient { Id = 702, FullName = "Nguyễn A", PatientCode = "PAT-2026-702", Status = "ACTIVE", Address = "Hà Nội" };
        var link = new PatientFamilyLink { OwnerPatientId = owner.Id, MemberPatientId = member.Id, Relationship = "CHILD" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { FullName = name };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, member.Id.ToString(), request));
    }

    [Fact]
    public async Task UpdateFamilyMember_UnlinkedPatient_ThrowsNotFoundException()
    {
        var user = new User { Id = 613, Email = "updater13@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 711, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-711" };
        var otherPatient = new Patient { Id = 712, FullName = "Người Khác", PatientCode = "PAT-2026-712", Status = "ACTIVE", Address = "Hà Nội" };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, otherPatient);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { Address = "Hà Nội số 456" };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, otherPatient.Id.ToString(), request));
    }

    [Fact]
    public async Task UpdateFamilyMember_RevokedLink_ThrowsNotFoundException()
    {
        var user = new User { Id = 614, Email = "updater14@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 721, UserId = user.Id, User = user, Status = "ACTIVE", PatientCode = "PAT-2026-721" };
        var member = new Patient { Id = 722, FullName = "Nguyễn B", PatientCode = "PAT-2026-722", Status = "ACTIVE", Address = "Hà Nội" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            RevokedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { Address = "Hà Nội số 789" };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, member.Id.ToString(), request));
    }

    [Fact]
    public async Task UpdateFamilyMember_InvalidUserId_ThrowsUnauthorizedAccessException()
    {
        var request = new UpdateFamilyMemberRequest { Address = "Hà Nội số 123" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.UpdateFamilyMemberAsync(0, "FAM-001", request));
    }

    [Fact]
    public async Task UpdateFamilyMember_OwnerNotFound_ThrowsNotFoundException()
    {
        var request = new UpdateFamilyMemberRequest { Address = "Hà Nội số 123" };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.UpdateFamilyMemberAsync(99999, "FAM-001", request));
    }

    [Fact]
    public async Task UpdateFamilyMember_OwnerLocked_ThrowsUnauthorizedAccessException()
    {
        var user = new User { Id = 615, Email = "locked_updater@test.com", AccountStatus = AccountStatus.LOCKED };
        var owner = new Patient { Id = 731, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var request = new UpdateFamilyMemberRequest { Address = "Hà Nội số 123" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.UpdateFamilyMemberAsync(user.Id, "FAM-001", request));
    }

    [Fact]
    public async Task RemoveFamilyMember_ValidLinkedMember_SuccessfullySetsRevokedAt()
    {
        var user = new User { Id = 801, Email = "remove_owner1@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 901, UserId = user.Id, User = user, Status = "ACTIVE", FullName = "CHỦ HỘ 1" };
        var member = new Patient { Id = 902, FullName = "CON TRAI 1", Status = "ACTIVE", PatientCode = "PAT-2026-902" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            RevokedAt = null
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        await _sut.RemoveFamilyMemberAsync(user.Id, member.Id.ToString());

        var updatedLink = await _db.PatientFamilyLinks.FindAsync(owner.Id, member.Id);
        Assert.NotNull(updatedLink);
        Assert.NotNull(updatedLink!.RevokedAt);

        // Member is no longer returned in active list
        var activeList = await _sut.GetFamilyMembersAsync(user.Id, includeSelf: false);
        Assert.DoesNotContain(activeList, m => m.Id == member.Id.ToString() || m.PatientCode == member.PatientCode);
    }

    [Fact]
    public async Task RemoveFamilyMember_WithReason_SuccessfullyRemoves()
    {
        var user = new User { Id = 802, Email = "remove_owner2@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 903, UserId = user.Id, User = user, Status = "ACTIVE", FullName = "CHỦ HỘ 2" };
        var member = new Patient { Id = 904, FullName = "CON GÁI 2", Status = "ACTIVE", PatientCode = "PAT-2026-904" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "DAUGHTER",
            RevokedAt = null
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        await _sut.RemoveFamilyMemberAsync(user.Id, member.Id.ToString(), reason: "Người thân đã tự lập tài khoản riêng");

        var updatedLink = await _db.PatientFamilyLinks.FindAsync(owner.Id, member.Id);
        Assert.NotNull(updatedLink);
        Assert.NotNull(updatedLink!.RevokedAt);
    }

    [Fact]
    public async Task RemoveFamilyMember_ByPatientCode_SuccessfullyRemoves()
    {
        var user = new User { Id = 803, Email = "remove_owner3@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 905, UserId = user.Id, User = user, Status = "ACTIVE", FullName = "CHỦ HỘ 3" };
        var member = new Patient { Id = 906, FullName = "MẸ 3", Status = "ACTIVE", PatientCode = "PAT-2026-906" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "MOTHER",
            RevokedAt = null
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        await _sut.RemoveFamilyMemberAsync(user.Id, "PAT-2026-906");

        var updatedLink = await _db.PatientFamilyLinks.FindAsync(owner.Id, member.Id);
        Assert.NotNull(updatedLink);
        Assert.NotNull(updatedLink!.RevokedAt);
    }

    [Theory]
    [InlineData("self")]
    [InlineData("SELF")]
    [InlineData("907")]
    [InlineData("PAT-2026-907")]
    public async Task RemoveFamilyMember_Self_ThrowsValidationException(string identifier)
    {
        var user = new User { Id = 804, Email = "remove_self@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 907, UserId = user.Id, User = user, Status = "ACTIVE", FullName = "CHỦ HỘ TỰ GỠ", PatientCode = "PAT-2026-907" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RemoveFamilyMemberAsync(user.Id, identifier));

        Assert.Contains("chính bản thân", ex.Message);
    }

    [Fact]
    public async Task RemoveFamilyMember_BlockedByActiveAppointment_ThrowsValidationException()
    {
        var user = new User { Id = 805, Email = "remove_appt@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 908, UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 909, FullName = "NGƯỜI THÂN CÓ LỊCH HẸN", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            RevokedAt = null
        };

        var appt = new Appointment
        {
            Id = 401,
            PatientId = member.Id,
            Status = "CONFIRMED",
            BookedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RemoveFamilyMemberAsync(user.Id, member.Id.ToString()));

        Assert.Contains("lịch hẹn khám chưa hoàn tất", ex.Message);
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("COMPLETED")]
    [InlineData("NO_SHOW")]
    public async Task RemoveFamilyMember_WithInactiveAppointments_SuccessfullyRemoves(string status)
    {
        var user = new User { Id = 806 + (int)status[0], Email = $"remove_done_{status}@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 910 + (int)status[0], UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 911 + (int)status[0], FullName = "NGƯỜI THÂN LỊCH HẸN ĐÃ XONG", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            RevokedAt = null
        };

        var appt = new Appointment
        {
            Id = 500 + (int)status[0],
            PatientId = member.Id,
            Status = status,
            BookedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync();

        await _sut.RemoveFamilyMemberAsync(user.Id, member.Id.ToString());

        var updatedLink = await _db.PatientFamilyLinks.FindAsync(owner.Id, member.Id);
        Assert.NotNull(updatedLink);
        Assert.NotNull(updatedLink!.RevokedAt);
    }

    [Fact]
    public async Task RemoveFamilyMember_NonExistentMember_ThrowsNotFoundException()
    {
        var user = new User { Id = 820, Email = "remove_missing@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 920, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.RemoveFamilyMemberAsync(user.Id, "99999"));
    }

    [Fact]
    public async Task RemoveFamilyMember_AlreadyRevokedMember_ThrowsNotFoundException()
    {
        var user = new User { Id = 821, Email = "remove_revoked@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 921, UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 922, FullName = "ĐÃ BỊ GỠ TỪ TRƯỚC", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            RevokedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.RemoveFamilyMemberAsync(user.Id, member.Id.ToString()));
    }

    [Fact]
    public async Task RemoveFamilyMember_MemberOfAnotherOwner_ThrowsNotFoundException()
    {
        var user1 = new User { Id = 822, Email = "owner_a@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner1 = new Patient { Id = 923, UserId = user1.Id, User = user1, Status = "ACTIVE" };

        var user2 = new User { Id = 823, Email = "owner_b@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner2 = new Patient { Id = 924, UserId = user2.Id, User = user2, Status = "ACTIVE" };

        var memberOfOwner2 = new Patient { Id = 925, FullName = "CON CỦA OWNER 2", Status = "ACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner2.Id,
            MemberPatientId = memberOfOwner2.Id,
            Relationship = "CHILD",
            RevokedAt = null
        };

        _db.Users.AddRange(user1, user2);
        _db.Patients.AddRange(owner1, owner2, memberOfOwner2);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        // user1 tries to remove owner2's family member
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.RemoveFamilyMemberAsync(user1.Id, memberOfOwner2.Id.ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveFamilyMember_EmptyIdentifier_ThrowsValidationException(string? identifier)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RemoveFamilyMemberAsync(1, identifier!));
    }

    [Fact]
    public async Task RemoveFamilyMember_InvalidUserId_ThrowsUnauthorizedAccessException()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.RemoveFamilyMemberAsync(0, "FAM-001"));
    }

    [Fact]
    public async Task RemoveFamilyMember_OwnerNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.RemoveFamilyMemberAsync(99999, "FAM-001"));
    }

    [Fact]
    public async Task RemoveFamilyMember_OwnerLocked_ThrowsUnauthorizedAccessException()
    {
        var user = new User { Id = 824, Email = "locked_remover@test.com", AccountStatus = AccountStatus.LOCKED };
        var owner = new Patient { Id = 926, UserId = user.Id, User = user, Status = "ACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.RemoveFamilyMemberAsync(user.Id, "FAM-001"));
    }

    [Fact]
    public async Task RemoveFamilyMember_OwnerInactive_ThrowsUnauthorizedAccessException()
    {
        var user = new User { Id = 825, Email = "inactive_patient_remover@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 927, UserId = user.Id, User = user, Status = "INACTIVE" };

        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.RemoveFamilyMemberAsync(user.Id, "FAM-001"));
    }

    [Fact]
    public async Task RemoveFamilyMember_MemberInactiveStatus_ThrowsNotFoundException()
    {
        var user = new User { Id = 826, Email = "remove_inactive_mem@test.com", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient { Id = 928, UserId = user.Id, User = user, Status = "ACTIVE" };
        var member = new Patient { Id = 929, FullName = "NGƯỜI THÂN ĐÃ DEACTIVATE", Status = "INACTIVE" };
        var link = new PatientFamilyLink
        {
            OwnerPatientId = owner.Id,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            RevokedAt = null
        };

        _db.Users.Add(user);
        _db.Patients.AddRange(owner, member);
        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.RemoveFamilyMemberAsync(user.Id, member.Id.ToString()));
    }
}
