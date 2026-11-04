using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Exceptions;

namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentDetailServiceTests : IDisposable
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

    private readonly AppointmentReadTestDb.Context _db;

    public AppointmentDetailServiceTests()
    {
        _db = AppointmentReadTestDb.CreateSeeded();
    }

    public void Dispose() => _db.Dispose();

    private AppointmentDetailService CreateSut(IAppointmentPolicyService? policyService = null)
        => new(
            _db,
            policyService ?? new StubPolicyService(),
            new AppointmentReadTestDb.FixedTimeProvider(AppointmentReadTestDb.DefaultUtcNow));

    [Fact]
    public async Task GetMyAppointmentDetailAsync_ByNumericId_UpcomingSelfConfirmed_ReturnsPhiComputedQueueAndAllUpcomingActions()
    {
        var sut = CreateSut();

        var detail = await sut.GetMyAppointmentDetailAsync(1, "103");

        Assert.Equal("103", detail.Id);
        Assert.Equal("APT-202611-0103", detail.AppointmentCode);
        Assert.Equal("CONFIRMED", detail.Status);
        Assert.Equal("001", detail.QueueNumber);
        Assert.Equal("Bản thân", detail.PatientRelationshipLabel);
        Assert.Equal("0912345678", detail.PatientPhone);
        Assert.Equal("Hà Nội", detail.PatientAddress);
        Assert.Equal("1990 (36 tuổi)", detail.PatientBirthYearLabel);
        Assert.Equal("15/05/1990 (36 tuổi) • Nam", detail.PatientBirthDateLabel);
        Assert.Equal(
            ["CANCEL", "RESCHEDULE", "UPDATE_REASON", "PRINT_SLIP", "TRACK_QUEUE"],
            detail.AvailableActions);
    }

    [Fact]
    public async Task GetMyAppointmentDetailAsync_ByCodeWithHashCaseInsensitive_ChildMemberAndPolicyHidesReschedule()
    {
        // Slot 11 starts at 2026-11-03 09:00 (+25h from 2026-11-02 08:00), < 48h reschedule minimum
        var sut = CreateSut(new StubPolicyService(cancelMinimumHours: 6, rescheduleMinimumHours: 48));

        var detail = await sut.GetMyAppointmentDetailAsync(1, "  #apt-202611-0102 ");

        Assert.Equal("102", detail.Id);
        Assert.Equal("APT-202611-0102", detail.AppointmentCode);
        Assert.Equal("Con", detail.PatientRelationshipLabel);
        Assert.Equal("007", detail.QueueNumber);
        Assert.Equal(
            ["CANCEL", "UPDATE_REASON", "PRINT_SLIP", "TRACK_QUEUE"],
            detail.AvailableActions);
    }

    [Fact]
    public async Task GetMyAppointmentDetailAsync_CompletedAppointment_ReturnsPrintSlipAndRebookOnly()
    {
        var sut = CreateSut();

        var detail = await sut.GetMyAppointmentDetailAsync(1, "101");

        Assert.Equal("COMPLETED", detail.Status);
        Assert.Equal("PAID_VIETQR", detail.PaymentStatus);
        Assert.Null(detail.QueueNumber);
        Assert.Equal(["PRINT_SLIP", "REBOOK"], detail.AvailableActions);
    }

    [Fact]
    public async Task GetMyAppointmentDetailAsync_CancelledAppointment_ReturnsRebookOnlyAndRefundedOrFree()
    {
        var sut = CreateSut();

        var detail = await sut.GetMyAppointmentDetailAsync(1, "104");

        Assert.Equal("CANCELLED", detail.Status);
        Assert.Equal("REFUNDED_OR_FREE", detail.PaymentStatus);
        Assert.Equal(["REBOOK"], detail.AvailableActions);
    }

    [Theory]
    [InlineData("106")]
    [InlineData("105")]
    [InlineData("APT-OTHER-0106")]
    public async Task GetMyAppointmentDetailAsync_OtherPatientOrRevokedMember_ThrowsForbiddenException(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetMyAppointmentDetailAsync(1, idOrCode));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("APT-NOPE")]
    public async Task GetMyAppointmentDetailAsync_NonExistentAppointment_ThrowsNotFoundException(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetMyAppointmentDetailAsync(1, idOrCode));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("0")]
    [InlineData("99999999999999999999")]
    public async Task GetMyAppointmentDetailAsync_InvalidIdentifier_ThrowsValidationException(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentDetailAsync(1, idOrCode));
    }

    [Fact]
    public async Task GetMyAppointmentDetailAsync_IdentifierExceeding40Chars_ThrowsValidationException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentDetailAsync(1, new string('A', 41)));
    }

    [Fact]
    public async Task GetMyAppointmentDetailAsync_InvalidUserId_ThrowsUnauthorizedAccessException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.GetMyAppointmentDetailAsync(0, "101"));
    }
}
