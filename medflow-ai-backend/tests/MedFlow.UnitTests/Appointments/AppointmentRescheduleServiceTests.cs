using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentRescheduleServiceTests : IDisposable
{
    private sealed class StubPolicyService : IAppointmentPolicyService
    {
        private readonly int _rescheduleMinimumHours;
        private readonly int _bookingLeadHours;
        private readonly int _maxActiveBookings;

        public StubPolicyService(
            int rescheduleMinimumHours = 12,
            int bookingLeadHours = 2,
            int maxActiveBookings = 3)
        {
            _rescheduleMinimumHours = rescheduleMinimumHours;
            _bookingLeadHours = bookingLeadHours;
            _maxActiveBookings = maxActiveBookings;
        }

        public Task<AppointmentPolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default)
            => Task.FromResult(new AppointmentPolicyDto(
                BookingWindowDays: 30,
                BookingLeadHours: _bookingLeadHours,
                MaxActiveBookings: _maxActiveBookings,
                RescheduleMinimumHours: _rescheduleMinimumHours,
                MaxReschedules: 2,
                CancelMinimumHours: 6,
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

    private readonly AppointmentReadTestDb.Context _db;
    private readonly RecordingBookingLock _lock = new();

    public AppointmentRescheduleServiceTests()
    {
        _db = AppointmentReadTestDb.CreateSeeded();
    }

    public void Dispose() => _db.Dispose();

    private AppointmentRescheduleService CreateSut(IAppointmentPolicyService? policyService = null)
    {
        var policy = policyService ?? new StubPolicyService();
        var timeProvider = new AppointmentReadTestDb.FixedTimeProvider(AppointmentReadTestDb.DefaultUtcNow);
        return new AppointmentRescheduleService(
            _db,
            policy,
            _lock,
            new AuditLogger(_db),
            new AppointmentDetailService(_db, policy, timeProvider),
            timeProvider);
    }

    private static RescheduleAppointmentRequest Req(string? targetSlotId, string? note = null)
        => new() { TargetSlotId = targetSlotId, RescheduleNote = note };

    private ExaminationSlot AddSlot(
        long id,
        long serviceId = 1,
        long? doctorId = 1,
        long? roomId = 1,
        string status = "OPEN",
        int capacity = 2,
        DateTime? startsAt = null,
        DateTime? endsAt = null)
    {
        var slot = new ExaminationSlot
        {
            Id = id,
            ServiceId = serviceId,
            DoctorId = doctorId,
            RoomId = roomId,
            StartsAt = startsAt ?? new DateTime(2026, 11, 5, 9, 0, 0),
            EndsAt = endsAt ?? new DateTime(2026, 11, 5, 9, 30, 0),
            Capacity = capacity,
            Status = status,
        };
        _db.ExaminationSlots.Add(slot);
        return slot;
    }

    private async Task SaveAsync() => await _db.SaveChangesAsync();

    [Fact]
    public async Task RescheduleAsync_HappyPath_CancelsOldAndCreatesNewWithAudit()
    {
        AddSlot(14);
        await SaveAsync();
        var sut = CreateSut();
        var beforeCount = await _db.Appointments.CountAsync();

        var dto = await sut.RescheduleAsync(1, "103", Req("14", "  Bận họp  "), "127.0.0.1");

        var old = await _db.Appointments.AsNoTracking().SingleAsync(a => a.Id == 103);
        Assert.Equal("CANCELLED", old.Status);

        var newAppt = await _db.Appointments.AsNoTracking()
            .SingleAsync(a => a.SlotId == 14 && a.Status == "BOOKED");
        Assert.Equal(1, newAppt.PatientId);
        Assert.Equal("ONLINE", newAppt.BookingChannel);
        Assert.Equal(1, newAppt.BookedByUserId);
        Assert.Equal(new DateTime(2026, 11, 2, 8, 0, 0), newAppt.BookedAt);
        Assert.Equal(old.ReasonForVisit, newAppt.ReasonForVisit);
        Assert.Equal($"APT-202611-{newAppt.Id:D4}", newAppt.AppointmentCode);
        Assert.Null(newAppt.IdempotencyKey);
        Assert.Null(newAppt.ReceptionNote);
        Assert.Equal(beforeCount + 1, await _db.Appointments.CountAsync());

        Assert.Equal(newAppt.Id.ToString(CultureInfo.InvariantCulture), dto.Id);
        Assert.Equal("BOOKED", dto.Status);
        Assert.Equal("001", dto.QueueNumber);

        Assert.Equal(new[] { (1L, (long?)1L, 14L) }, _lock.Calls);

        var log = await _db.AuditLogs.AsNoTracking().SingleAsync(l => l.Action == "APPOINTMENT_RESCHEDULE");
        Assert.Equal("APPOINTMENT", log.EntityType);
        Assert.Equal("103", log.EntityId);
        Assert.Equal(1, log.ActorUserId);
        using var doc = JsonDocument.Parse(log.ChangeData!);
        var root = doc.RootElement;
        Assert.Equal("Bận họp", root.GetProperty("RescheduleNote").GetString());
        Assert.Equal(14, root.GetProperty("ToSlotId").GetInt64());
        Assert.Equal(103, root.GetProperty("FromAppointmentId").GetInt64());
    }

    [Fact]
    public async Task RescheduleAsync_ByCode_FamilyMember_Succeeds()
    {
        AddSlot(14);
        await SaveAsync();
        var sut = CreateSut();

        var dto = await sut.RescheduleAsync(1, "apt-202611-0102", Req("14"), null);

        var old = await _db.Appointments.AsNoTracking().SingleAsync(a => a.Id == 102);
        Assert.Equal("CANCELLED", old.Status);
        var newAppt = await _db.Appointments.AsNoTracking().SingleAsync(a => a.SlotId == 14);
        Assert.Equal(3, newAppt.PatientId);
        Assert.Equal(newAppt.Id.ToString(CultureInfo.InvariantCulture), dto.Id);
    }

    [Theory]
    [InlineData("106")]
    [InlineData("105")]
    public async Task RescheduleAsync_NotOwned_ThrowsForbiddenAndNothingPersisted(string idOrCode)
    {
        AddSlot(14);
        await SaveAsync();
        var sut = CreateSut();
        var beforeCount = await _db.Appointments.CountAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => sut.RescheduleAsync(1, idOrCode, Req("14"), null));

        Assert.Equal(beforeCount, await _db.Appointments.CountAsync());
        var other = await _db.Appointments.AsNoTracking().SingleAsync(a => a.Id == long.Parse(idOrCode, CultureInfo.InvariantCulture));
        Assert.Equal("BOOKED", other.Status);
    }

    [Fact]
    public async Task RescheduleAsync_UnknownId_ThrowsNotFound()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.RescheduleAsync(1, "999", Req("14"), null));
    }

    [Fact]
    public async Task RescheduleAsync_BlankIdentifier_ThrowsValidation()
    {
        var sut = CreateSut();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => sut.RescheduleAsync(1, " ", Req("14"), null));
        Assert.Equal("Mã lịch hẹn không hợp lệ.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task RescheduleAsync_BadTargetSlotId_ThrowsValidation(string? targetSlotId)
    {
        var sut = CreateSut();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sut.RescheduleAsync(1, "103", Req(targetSlotId), null));
        Assert.Equal("Vui lòng chọn khung giờ mới hợp lệ để đổi lịch.", ex.Message);
    }

    [Fact]
    public async Task RescheduleAsync_NoteOver250_ThrowsValidation()
    {
        var sut = CreateSut();
        var note = new string('x', 251);
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sut.RescheduleAsync(1, "103", Req("14", note), null));
        Assert.Equal("Ghi chú đổi lịch tối đa 250 ký tự.", ex.Message);
    }

    [Theory]
    [InlineData("104")]
    [InlineData("101")]
    public async Task RescheduleAsync_NotModifiable_ThrowsConflict(string idOrCode)
    {
        var sut = CreateSut();
        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => sut.RescheduleAsync(1, idOrCode, Req("14"), null));
        Assert.Equal("Lịch hẹn ở trạng thái hiện tại không thể đổi lịch.", ex.Message);
    }

    [Fact]
    public async Task RescheduleAsync_InsideRescheduleMinimumHours_ThrowsValidation()
    {
        var sut = CreateSut(new StubPolicyService(rescheduleMinimumHours: 48));
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sut.RescheduleAsync(1, "102", Req("14"), null));
        Assert.Equal("Chỉ được đổi lịch trước giờ khám tối thiểu 48 giờ.", ex.Message);
    }

    [Fact]
    public async Task RescheduleAsync_SameSlot_ThrowsValidation()
    {
        var sut = CreateSut();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sut.RescheduleAsync(1, "103", Req("12"), null));
        Assert.Equal("Khung giờ mới trùng với khung giờ hiện tại của lịch hẹn.", ex.Message);
    }

    [Fact]
    public async Task RescheduleAsync_DifferentDoctor_ThrowsValidation()
    {
        _db.Doctors.Add(new Doctor { Id = 2, DoctorCode = "DOC-002", HospitalId = 1, StaffId = null, SpecialtyId = 1 });
        AddSlot(15, doctorId: 2);
        await SaveAsync();
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sut.RescheduleAsync(1, "103", Req("15"), null));
        Assert.Equal("Khung giờ mới phải cùng dịch vụ và bác sĩ với lịch hẹn hiện tại.", ex.Message);
    }

    [Fact]
    public async Task RescheduleAsync_DifferentService_ThrowsValidation()
    {
        AddSlot(16, serviceId: 2);
        await SaveAsync();
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sut.RescheduleAsync(1, "103", Req("16"), null));
        Assert.Equal("Khung giờ mới phải cùng dịch vụ và bác sĩ với lịch hẹn hiện tại.", ex.Message);
    }

    [Fact]
    public async Task RescheduleAsync_FullTargetSlot_ThrowsConflictAndOldStaysConfirmed()
    {
        AddSlot(14, capacity: 1);
        _db.Appointments.Add(new Appointment
        {
            Id = 200,
            PatientId = 2,
            SlotId = 14,
            BookingChannel = "ONLINE",
            Status = "BOOKED",
            BookedAt = new DateTime(2026, 11, 1, 10, 0, 0),
        });
        await SaveAsync();
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => sut.RescheduleAsync(1, "103", Req("14"), null));
        Assert.Equal("Khung giờ khám vừa hết chỗ. Vui lòng chọn khung giờ khác.", ex.Message);

        var old = await _db.Appointments.AsNoTracking().SingleAsync(a => a.Id == 103);
        Assert.Equal("CONFIRMED", old.Status);
        Assert.Equal(1, await _db.Appointments.CountAsync(a => a.SlotId == 14));
    }

    [Fact]
    public async Task RescheduleAsync_ClosedTargetSlot_ThrowsConflict()
    {
        AddSlot(14, status: "CLOSED");
        await SaveAsync();
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => sut.RescheduleAsync(1, "103", Req("14"), null));
        Assert.Equal("Khung giờ khám không còn mở để đặt lịch.", ex.Message);
    }

    [Fact]
    public async Task RescheduleAsync_TargetOverlappingOldSlot_StillSucceeds()
    {
        // Slot 16 overlaps old slot 12 (11-04 14:00-14:30) — exclusion lets it pass.
        AddSlot(16, startsAt: new DateTime(2026, 11, 4, 14, 15, 0), endsAt: new DateTime(2026, 11, 4, 14, 45, 0));
        await SaveAsync();
        var sut = CreateSut();

        var dto = await sut.RescheduleAsync(1, "103", Req("16"), null);

        var old = await _db.Appointments.AsNoTracking().SingleAsync(a => a.Id == 103);
        Assert.Equal("CANCELLED", old.Status);
        Assert.Equal("BOOKED", dto.Status);
    }

    [Fact]
    public async Task RescheduleAsync_PatientAtMaxActive_StillSucceedsBecauseOldExcluded()
    {
        AddSlot(14);
        await SaveAsync();
        // Patient 1 already has active appts 103 (CONFIRMED) — at max=1 the old one must be excluded.
        var sut = CreateSut(new StubPolicyService(maxActiveBookings: 1));

        var dto = await sut.RescheduleAsync(1, "103", Req("14"), null);

        var old = await _db.Appointments.AsNoTracking().SingleAsync(a => a.Id == 103);
        Assert.Equal("CANCELLED", old.Status);
        Assert.Equal("BOOKED", dto.Status);
    }
}
