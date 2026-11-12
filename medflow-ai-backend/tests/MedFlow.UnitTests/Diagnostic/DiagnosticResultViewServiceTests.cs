using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MedFlow.UnitTests.Diagnostic;

public sealed class DiagnosticResultViewServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTime FixedNowUtc = new(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc);

    private readonly DiagnosticResultEntryServiceTests.DiagnosticResultEntryTestDbContext _db;
    private readonly DiagnosticResultEntryService _service;

    public DiagnosticResultViewServiceTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticResultEntryServiceTests.DiagnosticResultEntryTestDbContext>()
            .UseInMemoryDatabase($"DiagnosticResultViewTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new DiagnosticResultEntryServiceTests.DiagnosticResultEntryTestDbContext(options);
        DiagnosticResultEntryServiceTests.SeedData(_db);
        _service = new DiagnosticResultEntryService(_db, new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetDiagnosticResultViewsAsync_ReturnsStoredResultAndHidesOrdersWithoutResultRow()
    {
        var response = await _service.GetDiagnosticResultViewsAsync(7);

        var finalized = Assert.Single(response.Items, i => i.OrderId == 1);
        Assert.Equal("DIA-202610-0001", finalized.OrderCode);
        Assert.Equal("WBC: 8.2 G/L; RBC: 4.6 T/L; HGB: 142 g/L; PLT: 250 G/L.", finalized.ResultText);
        Assert.Equal("Công thức máu trong giới hạn bình thường.", finalized.Conclusion);
        Assert.Equal("https://cdn.medflow.vn/reports/cbc-0001.pdf", finalized.Document.ReportFileUrl);
        Assert.True(finalized.Document.HasDocument);
        Assert.Equal("cbc-0001.pdf", finalized.Document.FileName);
        Assert.Equal("Không tiền sử dị ứng thuốc", finalized.Patient.Allergies);
        Assert.Equal("FINALIZED", finalized.ResultStatus);
        Assert.Equal(5, finalized.EnteredByStaffId);
        Assert.Equal("Hoàng Văn Long", finalized.EnteredByStaffName);

        Assert.Contains(response.Items, i => i.OrderId == 5 && i.ResultStatus == "PENDING_VERIFY");
        Assert.DoesNotContain(response.Items, i => i.OrderId is 2 or 3 or 4 or 6 or 7 or 8);
        Assert.Equal(5, response.Items[0].OrderId);
        Assert.Equal(1, response.Items[1].OrderId);
        Assert.Equal(2, response.Summary.TotalWithResult);
        Assert.Equal(1, response.Summary.FinalizedOrVerifiedCount);
        Assert.Equal(1, response.Summary.PendingVerificationCount);
    }

    [Fact]
    public async Task GetDiagnosticResultViewsAsync_KeywordDoesNotShrinkSummary()
    {
        var response = await _service.GetDiagnosticResultViewsAsync(
            7,
            new GetDiagnosticResultViewsRequest(Keyword: "WBC: 8.2"));

        var item = Assert.Single(response.Items);
        Assert.Equal(1, item.OrderId);
        Assert.Equal(2, response.Summary.TotalWithResult);
    }

    [Fact]
    public async Task GetDiagnosticResultViewsAsync_CompletedFilter_KeepsFinalizedOnly()
    {
        var response = await _service.GetDiagnosticResultViewsAsync(
            7,
            new GetDiagnosticResultViewsRequest(ResultStatus: "COMPLETED"));

        var item = Assert.Single(response.Items);
        Assert.Equal(1, item.OrderId);
        Assert.True(item.IsResultFinalized);
    }

    [Fact]
    public async Task GetDiagnosticResultViewsAsync_IncludesDraftAndCancelledRows()
    {
        _db.DiagnosticOrders.AddRange(
            new DiagnosticOrder
            {
                Id = 90,
                OrderCode = "DIA-202610-0090",
                VisitId = 101,
                OrderingDoctorId = 1,
                ServiceId = 10,
                Priority = "NORMAL",
                Status = "CANCELLED",
                OrderedAt = new DateTime(2026, 10, 20, 9, 0, 0),
            },
            new DiagnosticOrder
            {
                Id = 91,
                OrderCode = "DIA-202610-0091",
                VisitId = 101,
                OrderingDoctorId = 1,
                ServiceId = 10,
                Priority = "NORMAL",
                Status = "IN_PROGRESS",
                OrderedAt = new DateTime(2026, 10, 20, 9, 5, 0),
            });
        _db.DiagnosticResults.AddRange(
            new DiagnosticResult { Id = 90, DiagnosticOrderId = 90, Status = "FINALIZED" },
            new DiagnosticResult { Id = 91, DiagnosticOrderId = 91, Status = "DRAFT" });
        _db.DiagnosticResultVersions.AddRange(
            new DiagnosticResultVersion
            {
                Id = 90,
                DiagnosticResultId = 90,
                VersionNo = 1,
                ResultText = "Kết quả đã hủy vẫn còn.",
                Conclusion = "Đã hủy sau khi có kết quả.",
                EnteredByStaffId = 5,
                EnteredAt = new DateTime(2026, 10, 20, 9, 1, 0),
                IsFinalized = true,
            },
            new DiagnosticResultVersion
            {
                Id = 91,
                DiagnosticResultId = 91,
                VersionNo = 1,
                ResultText = "Bản nháp huyết học.",
                Conclusion = "Chưa khóa.",
                EnteredByStaffId = 5,
                EnteredAt = new DateTime(2026, 10, 20, 9, 6, 0),
                IsFinalized = false,
            });
        await _db.SaveChangesAsync();

        var response = await _service.GetDiagnosticResultViewsAsync(7);

        Assert.Contains(response.Items, i => i.OrderId == 91 && i.ResultStatus == "DRAFT");
        Assert.Contains(response.Items, i => i.OrderId == 90 && i.ResultStatus == "CANCELLED");
        Assert.Equal(1, response.Summary.DraftOrPreliminaryCount);
        Assert.Equal(1, response.Summary.CancelledCount);
    }

    [Fact]
    public async Task GetDiagnosticResultViewByIdOrCodeAsync_ReturnsVersionHistoryAndDocument()
    {
        var auditsBefore = await _db.AuditLogs.CountAsync();
        var stateVersion = (await _db.QueueEntries.SingleAsync(q => q.Id == 11)).StateVersion;

        var detail = await _service.GetDiagnosticResultViewByIdOrCodeAsync(7, "DIA-202610-0001");

        Assert.Equal("DIA-202610-0001", detail.Item.OrderCode);
        Assert.Equal("WBC: 8.2 G/L; RBC: 4.6 T/L; HGB: 142 g/L; PLT: 250 G/L.", detail.Item.ResultText);
        Assert.Equal("J18.9", detail.ClinicalContext.PrimaryDiagnosisCode);
        Assert.Equal("Không tiền sử dị ứng thuốc", detail.Item.Patient.Allergies);
        var version = Assert.Single(detail.Versions);
        Assert.Equal(1, version.VersionNo);
        Assert.Equal("cbc-0001.pdf", detail.Item.Document.FileName);
        Assert.Equal(auditsBefore, await _db.AuditLogs.CountAsync());
        Assert.Equal(stateVersion, (await _db.QueueEntries.SingleAsync(q => q.Id == 11)).StateVersion);
    }

    [Fact]
    public async Task GetDiagnosticResultViewByIdOrCodeAsync_OrderWithoutResult_ThrowsNotFound()
    {
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDiagnosticResultViewByIdOrCodeAsync(7, "DIA-202610-0002"));

        Assert.Equal("Phiếu chỉ định chưa có kết quả cận lâm sàng.", ex.Message);
    }

    [Fact]
    public async Task GetDiagnosticResultViewByIdOrCodeAsync_OtherHospital_ThrowsForbidden()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticResultViewByIdOrCodeAsync(7, "DIA-202610-0008"));

        Assert.Equal(
            "Phiếu chỉ định cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.",
            ex.Message);
    }

    [Fact]
    public async Task GetDiagnosticResultViewByIdOrCodeAsync_InactiveService_StillReturnsFinalizedResult()
    {
        _db.DiagnosticOrders.Add(new DiagnosticOrder
        {
            Id = 92,
            OrderCode = "DIA-202610-0092",
            VisitId = 100,
            OrderingDoctorId = 1,
            ServiceId = 14,
            Priority = "NORMAL",
            Status = "COMPLETED",
            OrderedAt = new DateTime(2026, 10, 20, 9, 10, 0),
        });
        _db.DiagnosticResults.Add(new DiagnosticResult
        {
            Id = 92,
            DiagnosticOrderId = 92,
            Status = "FINALIZED",
            FinalizedAt = new DateTime(2026, 10, 20, 9, 20, 0),
            FinalizedByStaffId = 5,
        });
        _db.DiagnosticResultVersions.Add(new DiagnosticResultVersion
        {
            Id = 92,
            DiagnosticResultId = 92,
            VersionNo = 1,
            ResultText = "Kết quả dịch vụ đã ngừng.",
            Conclusion = "Vẫn đọc được.",
            ReportFileUrl = "https://files.example/reports/inactive.pdf",
            EnteredByStaffId = 5,
            EnteredAt = new DateTime(2026, 10, 20, 9, 20, 0),
            IsFinalized = true,
            VerifiedByStaffId = 5,
            VerifiedAt = new DateTime(2026, 10, 20, 9, 20, 0),
        });
        await _db.SaveChangesAsync();

        var detail = await _service.GetDiagnosticResultViewByIdOrCodeAsync(7, "DIA-202610-0092");

        Assert.Equal("Kết quả dịch vụ đã ngừng.", detail.Item.ResultText);
        Assert.True(detail.Item.IsResultFinalized);
        Assert.Equal("inactive.pdf", detail.Item.Document.FileName);
    }

    [Fact]
    public async Task GetDiagnosticResultViewByIdOrCodeAsync_DecodesHttpFileNameAndClearsEmptyUrl()
    {
        var version = await _db.DiagnosticResultVersions.SingleAsync(v => v.Id == 1);
        version.ReportFileUrl = "https://files.example/reports/a%20b.pdf";
        await _db.SaveChangesAsync();

        var decoded = await _service.GetDiagnosticResultViewByIdOrCodeAsync(7, "DIA-202610-0001");
        Assert.Equal("https://files.example/reports/a%20b.pdf", decoded.Item.Document.ReportFileUrl);
        Assert.True(decoded.Item.Document.HasDocument);
        Assert.Equal("a b.pdf", decoded.Item.Document.FileName);

        version.ReportFileUrl = "   ";
        await _db.SaveChangesAsync();

        var cleared = await _service.GetDiagnosticResultViewByIdOrCodeAsync(7, "DIA-202610-0001");
        Assert.False(cleared.Item.Document.HasDocument);
        Assert.Null(cleared.Item.Document.ReportFileUrl);
        Assert.Null(cleared.Item.Document.FileName);
    }

    [Fact]
    public async Task GetDiagnosticResultViewsAsync_StaffWithoutReadPermission_ThrowsForbidden()
    {
        _db.Roles.Add(new Role { Id = 6, Code = "DIAGNOSTIC_STAFF", Name = "Diagnostic Staff Without Read" });
        _db.RolePermissions.Add(new RolePermission { RoleId = 6, PermissionId = 10 });
        _db.Users.Add(new User
        {
            Id = 15,
            Email = "diag_no_read@medflow.test",
            PasswordHash = "hash",
            AccountStatus = AccountStatus.ACTIVE,
        });
        _db.UserRoles.Add(new UserRole { UserId = 15, RoleId = 6 });
        _db.Staff.Add(new Staff
        {
            Id = 15,
            UserId = 15,
            HospitalId = 1,
            DepartmentId = 1,
            StaffCode = "DIA-015",
            FullName = "KTV Không Quyền Đọc",
            Status = "ACTIVE",
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticResultViewsAsync(15));

        Assert.Equal(
            "Nhân viên cận lâm sàng không có quyền truy cập danh sách kết quả cận lâm sàng.",
            ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GetDiagnosticResultViewsAsync_LimitOutOfRange_Throws(int limit)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticResultViewsAsync(7, new GetDiagnosticResultViewsRequest(Limit: limit)));

        Assert.Equal("Số lượng phiếu chỉ định cận lâm sàng hiển thị phải từ 1 đến 100.", ex.Message);
    }

    [Fact]
    public async Task GetDiagnosticResultViewsAsync_PendingStatusAndMarkupKeyword_Throw()
    {
        var pending = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticResultViewsAsync(7, new GetDiagnosticResultViewsRequest(ResultStatus: "PENDING")));
        Assert.Equal("Trạng thái kết quả cận lâm sàng lọc không hợp lệ.", pending.Message);

        var keyword = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticResultViewsAsync(7, new GetDiagnosticResultViewsRequest(Keyword: "a<b")));
        Assert.Equal("Từ khóa tìm kiếm phiếu chỉ định cận lâm sàng không hợp lệ.", keyword.Message);
    }
}
