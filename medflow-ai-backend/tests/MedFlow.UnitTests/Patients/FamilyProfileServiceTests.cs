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
}
