using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentCancelServiceTests : IDisposable
{
    private sealed class StubPolicyService : IAppointmentPolicyService
    {
        private readonly int _cancelMinimumHours;
        private readonly int _rescheduleMinimumHours;

        public StubPolicyService(
            int cancelMinimumHours = 6,
            int rescheduleMinimumHours = 12)
        {
            _cancelMinimumHours = cancelMinimumHours;
            _rescheduleMinimumHours = rescheduleMinimumHours;
        }

        public Task<AppointmentPolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default)
            => Task.FromResult(new AppointmentPolicyDto(
                BookingWindowDays: 30,
                BookingLeadHours: 2,
                MaxActiveBookings: 3,
                RescheduleMinimumHours: _rescheduleMinimumHours,
                MaxReschedules: 2,
                CancelMinimumHours: _cancelMinimumHours,
                NoShowGraceMinutes: 15,
                NoShowMaxCount: 3,
                NoShowLookbackDays: 90,
                NoShowBlockDays: 30,
                LastUpdatedAt: null));

        public Task<AppointmentPolicyDto> GetForAdminAsync(long userId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentPolicyDto> UpdateForAdminAsync(
            long userId,
            UpdateAppointmentPolicyRequest request,
            string? ipAddress,
            CancellationToken ct = default)
            => throw new NotSupportedException();
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

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<(long? Actor, string Action, string EntityType, string? EntityId, object? Data)> Entries { get; } = new();

        public void Add(
            long? actorUserId,
            string action,
            string entityType,
            string? entityId,
            object? changeData,
            string? ipAddress)
        {
            Entries.Add((actorUserId, action, entityType, entityId, changeData));
        }
    }

    private readonly AppointmentReadTestDb.Context _db;
    private readonly RecordingBookingLock _lock = new();
    private readonly RecordingAuditLogger _audit = new();

    public AppointmentCancelServiceTests()
    {
        _db = AppointmentReadTestDb.CreateSeeded();
    }

    public void Dispose() => _db.Dispose();

    private AppointmentCancelService CreateSut(IAppointmentPolicyService? policyService = null)
    {
        var policy = policyService ?? new StubPolicyService();
        var time = new AppointmentReadTestDb.FixedTimeProvider(AppointmentReadTestDb.DefaultUtcNow);
        var detail = new AppointmentDetailService(_db, policy, time);
        return new AppointmentCancelService(_db, policy, _lock, _audit, detail, time);
    }

    [Fact]
    public async Task CancelMyAppointmentAsync_SelfConfirmedAppointment_CancelsTrimsReasonLocksAndAudits()
    {
        var sut = CreateSut();

        var dto = await sut.CancelMyAppointmentAsync(
            userId: 1,
            idOrCode: "103",
            request: new CancelAppointmentRequest { CancellationReason = "  Bận công tác đột xuất  " },
            ipAddress: "127.0.0.1");

        Assert.Equal("CANCELLED", dto.Status);
        Assert.Equal("Bận công tác đột xuất", dto.CancellationReason);
        Assert.Equal(["REBOOK"], dto.AvailableActions);
        Assert.Equal("REFUNDED_OR_FREE", dto.PaymentStatus);

        var entity = await _db.Appointments.AsNoTracking().FirstAsync(a => a.Id == 103);
        Assert.Equal("CANCELLED", entity.Status);
        Assert.Equal("Bận công tác đột xuất", entity.CancellationReason);
        Assert.Equal(new DateTime(2026, 11, 2, 8, 0, 0), entity.CancelledAt);
        Assert.Equal(1L, entity.CancelledByUserId);

        var lockCall = Assert.Single(_lock.Calls);
        Assert.Equal((1L, (long?)null, 12L), lockCall);

        var auditEntry = Assert.Single(_audit.Entries);
        Assert.Equal(1L, auditEntry.Actor);
        Assert.Equal("APPOINTMENT_CANCEL", auditEntry.Action);
        Assert.Equal("APPOINTMENT", auditEntry.EntityType);
        Assert.Equal("103", auditEntry.EntityId);
    }

    [Fact]
    public async Task CancelMyAppointmentAsync_FamilyMemberByCode_ReleasesSlotCapacity()
    {
        var beforeActiveCount = await _db.Appointments
            .CountAsync(a => a.SlotId == 11 && (a.Status == null || a.Status.ToUpper() != "CANCELLED"));
        Assert.Equal(3, beforeActiveCount);

        var sut = CreateSut();

        var dto = await sut.CancelMyAppointmentAsync(
            userId: 1,
            idOrCode: "#apt-202611-0102",
            request: new CancelAppointmentRequest { CancellationReason = "Con bận lịch thi ở trường" },
            ipAddress: null);

        Assert.Equal("CANCELLED", dto.Status);
        Assert.Equal("Con bận lịch thi ở trường", dto.CancellationReason);

        var afterActiveCount = await _db.Appointments
            .CountAsync(a => a.SlotId == 11 && (a.Status == null || a.Status.ToUpper() != "CANCELLED"));
        Assert.Equal(2, afterActiveCount);
    }

    [Fact]
    public async Task CancelMyAppointmentAsync_WithinMinimumCancelWindow_ThrowsValidationAndDoesNotMutate()
    {
        var sut = CreateSut(new StubPolicyService(cancelMinimumHours: 48));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CancelMyAppointmentAsync(
                userId: 1,
                idOrCode: "102",
                request: new CancelAppointmentRequest { CancellationReason = "Không kịp sắp xếp thời gian" },
                ipAddress: null));

        Assert.Equal("Chỉ được hủy lịch trước giờ khám tối thiểu 48 giờ.", ex.Message);

        var entity = await _db.Appointments.AsNoTracking().FirstAsync(a => a.Id == 102);
        Assert.Equal("BOOKED", entity.Status);
        Assert.Null(entity.CancellationReason);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task CancelMyAppointmentAsync_AlreadyCancelled_ThrowsConflict()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CancelMyAppointmentAsync(
                userId: 1,
                idOrCode: "104",
                request: new CancelAppointmentRequest { CancellationReason = "Lý do hủy hợp lệ trên 10 ký tự" },
                ipAddress: null));

        Assert.Equal("Lịch hẹn đã được hủy trước đó.", ex.Message);
    }

    [Fact]
    public async Task CancelMyAppointmentAsync_CompletedVisitAppointment_ThrowsConflict()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CancelMyAppointmentAsync(
                userId: 1,
                idOrCode: "101",
                request: new CancelAppointmentRequest { CancellationReason = "Lý do hủy hợp lệ trên 10 ký tự" },
                ipAddress: null));

        Assert.Equal("Lịch hẹn ở trạng thái hiện tại không thể hủy.", ex.Message);
    }

    [Theory]
    [InlineData("105")]
    [InlineData("106")]
    public async Task CancelMyAppointmentAsync_RevokedOrUnownedAppointment_ThrowsForbidden(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.CancelMyAppointmentAsync(
                userId: 1,
                idOrCode: idOrCode,
                request: new CancelAppointmentRequest { CancellationReason = "Lý do hủy hợp lệ trên 10 ký tự" },
                ipAddress: null));
    }

    [Fact]
    public async Task CancelMyAppointmentAsync_UnknownAppointment_ThrowsNotFound()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.CancelMyAppointmentAsync(
                userId: 1,
                idOrCode: "999",
                request: new CancelAppointmentRequest { CancellationReason = "Lý do hủy hợp lệ trên 10 ký tự" },
                ipAddress: null));
    }

    public static TheoryData<string?> InvalidReasons => new()
    {
        null,
        "   ",
        "quá ngắn!",
        new string('a', 251),
    };

    [Theory]
    [MemberData(nameof(InvalidReasons))]
    public async Task CancelMyAppointmentAsync_InvalidReason_ThrowsValidationAndDoesNotMutate(string? reason)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CancelMyAppointmentAsync(
                userId: 1,
                idOrCode: "103",
                request: new CancelAppointmentRequest { CancellationReason = reason },
                ipAddress: null));

        var entity = await _db.Appointments.AsNoTracking().FirstAsync(a => a.Id == 103);
        Assert.Equal("CONFIRMED", entity.Status);
        Assert.Null(entity.CancellationReason);
    }

    [Fact]
    public async Task CancelMyAppointmentAsync_InvalidIdentifier_ThrowsValidation()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CancelMyAppointmentAsync(
                userId: 1,
                idOrCode: "0",
                request: new CancelAppointmentRequest { CancellationReason = "Lý do hủy hợp lệ trên 10 ký tự" },
                ipAddress: null));
    }

    [Fact]
    public async Task CancelMyAppointmentAsync_InvalidUserId_ThrowsUnauthorized()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.CancelMyAppointmentAsync(
                userId: 0,
                idOrCode: "103",
                request: new CancelAppointmentRequest { CancellationReason = "Lý do hủy hợp lệ trên 10 ký tự" },
                ipAddress: null));
    }
}
