using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;

namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentStatusTrackingServiceTests : IDisposable
{
    private readonly AppointmentReadTestDb.Context _db;

    public AppointmentStatusTrackingServiceTests()
    {
        _db = AppointmentReadTestDb.CreateSeeded();
    }

    public void Dispose() => _db.Dispose();

    private AppointmentStatusTrackingService CreateSut(TimeProvider? timeProvider = null)
        => new(
            _db,
            timeProvider ?? new AppointmentReadTestDb.FixedTimeProvider(AppointmentReadTestDb.DefaultUtcNow));

    [Fact]
    public async Task GetMyAppointmentStatusAsync_ConfirmedUpcoming_ReturnsCheckInNextStepWithDueAt()
    {
        var sut = CreateSut();

        var dto = await sut.GetMyAppointmentStatusAsync(1, "103");

        Assert.Equal("103", dto.AppointmentId);
        Assert.Equal("APT-202611-0103", dto.AppointmentCode);
        Assert.Equal("CONFIRMED", dto.Status);
        Assert.Equal(
            ["BOOKED", "CHECKED_IN", "IN_CONSULTATION", "COMPLETED"],
            dto.Steps.Select(s => s.Key).ToArray());
        Assert.Equal(
            ["done", "current", "upcoming", "upcoming"],
            dto.Steps.Select(s => s.State).ToArray());
        Assert.Equal("2026-11-01T11:00:00+07:00", dto.Steps[0].OccurredAt);
        Assert.Null(dto.Steps[1].OccurredAt);
        Assert.Null(dto.Steps[2].OccurredAt);
        Assert.Null(dto.Steps[3].OccurredAt);

        Assert.Equal("CHECK_IN", dto.NextStep.Code);
        Assert.Equal("2026-11-04T13:45:00+07:00", dto.NextStep.DueAt);
        Assert.Contains("13:45", dto.NextStep.Description);
        Assert.Contains("04/11/2026", dto.NextStep.Description);
    }

    [Fact]
    public async Task GetMyAppointmentStatusAsync_ChildMemberBooked_ReturnsConAndStoredQueueNumber()
    {
        var sut = CreateSut();

        var dto = await sut.GetMyAppointmentStatusAsync(1, "  #apt-202611-0102 ");

        Assert.Equal("102", dto.AppointmentId);
        Assert.Equal("Con", dto.PatientRelationshipLabel);
        Assert.Equal("007", dto.QueueNumber);
        Assert.Equal("CHECK_IN", dto.NextStep.Code);
    }

    [Fact]
    public async Task GetMyAppointmentStatusAsync_CompletedVisit_AllStepsDoneAndFollowUp()
    {
        var visit = _db.Visits.Single(v => v.Id == 501);
        visit.CheckedInAt = new DateTime(2026, 10, 20, 8, 55, 0);
        visit.CompletedAt = new DateTime(2026, 10, 20, 9, 30, 0);
        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 602,
            AppointmentId = 101,
            QueueType = "EXAMINATION",
            QueueNumber = 1,
            StartedAt = new DateTime(2026, 10, 20, 9, 5, 0),
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var dto = await sut.GetMyAppointmentStatusAsync(1, "101");

        Assert.Equal("COMPLETED", dto.Status);
        Assert.Equal(
            ["BOOKED", "CHECKED_IN", "IN_CONSULTATION", "COMPLETED"],
            dto.Steps.Select(s => s.Key).ToArray());
        Assert.All(dto.Steps, s => Assert.Equal("done", s.State));
        Assert.Equal("2026-10-15T10:00:00+07:00", dto.Steps[0].OccurredAt);
        Assert.Equal("2026-10-20T08:55:00+07:00", dto.Steps[1].OccurredAt);
        Assert.Equal("2026-10-20T09:05:00+07:00", dto.Steps[2].OccurredAt);
        Assert.Equal("2026-10-20T09:30:00+07:00", dto.Steps[3].OccurredAt);
        Assert.Equal("FOLLOW_UP", dto.NextStep.Code);
        Assert.Null(dto.NextStep.DueAt);
    }

    [Fact]
    public async Task GetMyAppointmentStatusAsync_CancelledAppointment_TwoDoneStepsAndRebook()
    {
        var appointment = _db.Appointments.Single(a => a.Id == 104);
        appointment.CancelledAt = new DateTime(2026, 10, 21, 9, 0, 0);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var dto = await sut.GetMyAppointmentStatusAsync(1, "104");

        Assert.Equal("CANCELLED", dto.Status);
        Assert.Equal(["BOOKED", "CANCELLED"], dto.Steps.Select(s => s.Key).ToArray());
        Assert.All(dto.Steps, s => Assert.Equal("done", s.State));
        Assert.Equal("2026-10-21T09:00:00+07:00", dto.Steps[1].OccurredAt);
        Assert.Equal("REBOOK", dto.NextStep.Code);
        Assert.Null(dto.NextStep.DueAt);
    }

    [Fact]
    public async Task GetMyAppointmentStatusAsync_CheckedInProgress_AdvancesThroughQueueMilestones()
    {
        var appointment = _db.Appointments.Single(a => a.Id == 103);
        appointment.Status = "CHECKED_IN";
        _db.Visits.Add(new Visit
        {
            Id = 502,
            AppointmentId = 103,
            Status = "WAITING",
            ReasonForVisit = "Khám",
            CheckedInAt = new DateTime(2026, 11, 4, 13, 50, 0),
        });
        var queue = new QueueEntry
        {
            Id = 603,
            AppointmentId = 103,
            QueueType = "EXAMINATION",
            QueueNumber = 3,
        };
        _db.QueueEntries.Add(queue);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var waiting = await sut.GetMyAppointmentStatusAsync(1, "103");
        Assert.Equal("CHECKED_IN", waiting.Status);
        Assert.Equal(
            ["done", "done", "current", "upcoming"],
            waiting.Steps.Select(s => s.State).ToArray());
        Assert.Equal("WAIT_FOR_CALL", waiting.NextStep.Code);
        Assert.Contains("gọi số 003", waiting.NextStep.Description);

        queue.CalledAt = new DateTime(2026, 11, 4, 14, 2, 0);
        await _db.SaveChangesAsync();

        var called = await sut.GetMyAppointmentStatusAsync(1, "103");
        Assert.Equal("GO_TO_ROOM", called.NextStep.Code);

        queue.StartedAt = new DateTime(2026, 11, 4, 14, 5, 0);
        await _db.SaveChangesAsync();

        var started = await sut.GetMyAppointmentStatusAsync(1, "103");
        Assert.Equal(
            ["done", "done", "done", "current"],
            started.Steps.Select(s => s.State).ToArray());
        Assert.Equal("IN_CONSULTATION", started.NextStep.Code);
    }

    [Fact]
    public async Task GetMyAppointmentStatusAsync_BookedPastStart_ReturnsContactReception()
    {
        var appointment = _db.Appointments.Single(a => a.Id == 103);
        appointment.Status = "BOOKED";
        await _db.SaveChangesAsync();

        // 2026-11-04T08:00:00Z = 15:00 VN, slot starts 14:00 VN -> past start
        var pastStart = new AppointmentReadTestDb.FixedTimeProvider(
            new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero));
        var sut = CreateSut(pastStart);

        var dto = await sut.GetMyAppointmentStatusAsync(1, "103");

        Assert.Equal("BOOKED", dto.Status);
        Assert.Equal("CONTACT_RECEPTION", dto.NextStep.Code);
        Assert.Null(dto.NextStep.DueAt);
    }

    [Theory]
    [InlineData("APT-REVOKED-0105")]
    [InlineData("APT-OTHER-0106")]
    public async Task GetMyAppointmentStatusAsync_OutOfScope_ThrowsForbiddenException(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetMyAppointmentStatusAsync(1, idOrCode));
    }

    [Theory]
    [InlineData("99999999")]
    [InlineData("APT-NOPE")]
    public async Task GetMyAppointmentStatusAsync_NonExistent_ThrowsNotFoundException(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetMyAppointmentStatusAsync(1, idOrCode));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("   ")]
    public async Task GetMyAppointmentStatusAsync_InvalidIdentifier_ThrowsValidationException(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentStatusAsync(1, idOrCode));
    }

    [Fact]
    public async Task GetMyAppointmentStatusAsync_InvalidUserId_ThrowsUnauthorizedAccessException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.GetMyAppointmentStatusAsync(0, "103"));
    }
}
