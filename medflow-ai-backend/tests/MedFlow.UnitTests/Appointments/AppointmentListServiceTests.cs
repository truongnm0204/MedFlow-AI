using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Exceptions;
namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentListServiceTests : IDisposable
{
    private readonly AppointmentReadTestDb.Context _db;

    public AppointmentListServiceTests()
    {
        _db = AppointmentReadTestDb.CreateSeeded();
    }

    public void Dispose() => _db.Dispose();

    private AppointmentListService CreateSut()
        => new(_db, new AppointmentReadTestDb.FixedTimeProvider(AppointmentReadTestDb.DefaultUtcNow));

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
