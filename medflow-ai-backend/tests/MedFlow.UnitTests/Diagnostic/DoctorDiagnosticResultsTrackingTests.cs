using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Services.Consultation;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MedFlow.UnitTests.Diagnostic;

public sealed class DoctorDiagnosticResultsTrackingTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTime FixedNowUtc = new(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc);

    private readonly DiagnosticResultEntryServiceTests.DiagnosticResultEntryTestDbContext _db;
    private readonly ConsultationWorkspaceService _service;

    public DoctorDiagnosticResultsTrackingTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticResultEntryServiceTests.DiagnosticResultEntryTestDbContext>()
            .UseInMemoryDatabase($"DoctorDiagnosticResultsTrackingTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new DiagnosticResultEntryServiceTests.DiagnosticResultEntryTestDbContext(options);
        DiagnosticResultEntryServiceTests.SeedData(_db);
        _service = new ConsultationWorkspaceService(_db, new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Default_ReturnsOwnedVisitsWithCompletionAndStats()
    {
        var response = await _service.GetDiagnosticResultsTrackingAsync(3);

        Assert.Equal(new DateOnly(2026, 10, 20), response.Date);
        Assert.Equal([101L, 100L], response.Items.Select(i => i.VisitId).ToArray());
        Assert.DoesNotContain(response.Items, i => i.VisitId is 102 or 103 or 104);

        var visit100 = Assert.Single(response.Items, i => i.VisitId == 100);
        Assert.Equal("PENDING_SIGN", visit100.TrackingStatus);
        Assert.Equal(2, visit100.TotalOrders);
        Assert.Equal(1, visit100.SignedOrders);
        Assert.Equal(1, visit100.AwaitingSignOrders);
        Assert.Equal(0, visit100.InProgressOrders);
        Assert.Equal(0, visit100.PendingOrders);
        Assert.Equal(50, visit100.CompletionPercent);

        var order1 = Assert.Single(visit100.Orders, o => o.OrderId == 1);
        Assert.Equal("SIGNED", order1.TrackingStatus);
        Assert.Equal(new DateTime(2026, 10, 20, 8, 20, 0), order1.SignedAt);
        Assert.Equal("Công thức máu trong giới hạn bình thường.", order1.ResultSummary);
        Assert.Equal("Hoàng Văn Long", order1.AssigneeStaffName);

        var order5 = Assert.Single(visit100.Orders, o => o.OrderId == 5);
        Assert.Equal("AWAITING_SIGN", order5.TrackingStatus);
        Assert.Null(order5.SignedAt);
        Assert.Equal("Chờ bác sĩ CĐHA duyệt kết luận cuối.", order5.ResultSummary);

        var visit101 = Assert.Single(response.Items, i => i.VisitId == 101);
        Assert.Equal("IN_PROGRESS", visit101.TrackingStatus);
        Assert.Equal(5, visit101.TotalOrders);
        Assert.DoesNotContain(visit101.Orders, o => o.OrderId == 7);
        Assert.Equal(1, visit101.SignedOrders);
        Assert.Equal(2, visit101.InProgressOrders);
        Assert.Equal(2, visit101.PendingOrders);
        Assert.Equal(20, visit101.CompletionPercent);

        Assert.Equal(2, response.Stats.TotalVisits);
        Assert.Equal(0, response.Stats.CompletedVisits);
        Assert.Equal(1, response.Stats.InProgressVisits);
        Assert.Equal(1, response.Stats.PendingSignVisits);
        Assert.Equal(7, response.Stats.TotalOrders);
        Assert.Equal(2, response.Stats.SignedOrders);
        Assert.Equal(5, response.Stats.WaitingOrders);
    }

    [Fact]
    public async Task FinalizingLastPendingResult_MovesVisitToCompleted()
    {
        var result2 = await _db.DiagnosticResults.FirstAsync(r => r.Id == 2);
        result2.Status = "FINALIZED";
        result2.FinalizedAt = new DateTime(2026, 10, 20, 8, 50, 0);
        result2.FinalizedByStaffId = 5;

        var version2 = await _db.DiagnosticResultVersions.FirstAsync(v => v.Id == 2);
        version2.IsFinalized = true;

        await _db.SaveChangesAsync();

        var response = await _service.GetDiagnosticResultsTrackingAsync(3);

        var visit100 = Assert.Single(response.Items, i => i.VisitId == 100);
        Assert.Equal("COMPLETED", visit100.TrackingStatus);
        Assert.Equal(100, visit100.CompletionPercent);

        var order5 = Assert.Single(visit100.Orders, o => o.OrderId == 5);
        Assert.Equal("SIGNED", order5.TrackingStatus);
        Assert.Equal(new DateTime(2026, 10, 20, 8, 50, 0), order5.SignedAt);

        Assert.Equal(1, response.Stats.CompletedVisits);
    }

    [Fact]
    public async Task ExcludesVisitsOwnedByAnotherDoctor()
    {
        _db.Visits.AddRange(
            new Visit
            {
                Id = 105,
                VisitCode = "VIS-202610-0105",
                PatientId = 10,
                AssignedDoctorId = 2,
                Status = "IN_PROGRESS",
                CheckedInAt = new DateTime(2026, 10, 20, 7, 45, 0),
            },
            new Visit
            {
                Id = 106,
                VisitCode = "VIS-202610-0106",
                PatientId = 10,
                AssignedDoctorId = null,
                CompletedByDoctorId = 1,
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 20, 6, 45, 0),
            });

        _db.DiagnosticOrders.AddRange(
            new DiagnosticOrder
            {
                Id = 50,
                OrderCode = "DIA-202610-0050",
                VisitId = 105,
                OrderingDoctorId = 2,
                ServiceId = 10,
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 20, 8, 0, 0),
            },
            new DiagnosticOrder
            {
                Id = 51,
                OrderCode = "DIA-202610-0051",
                VisitId = 106,
                OrderingDoctorId = 1,
                ServiceId = 10,
                Status = "COMPLETED",
                OrderedAt = new DateTime(2026, 10, 20, 7, 0, 0),
            });

        await _db.SaveChangesAsync();

        var response = await _service.GetDiagnosticResultsTrackingAsync(3);

        Assert.Contains(response.Items, i => i.VisitId == 106);
        Assert.DoesNotContain(response.Items, i => i.VisitId == 105);
    }

    [Fact]
    public async Task StatusKeywordAndPaging_FilterItemsButNotStats()
    {
        var byStatus = await _service.GetDiagnosticResultsTrackingAsync(
            3,
            new GetDiagnosticResultsTrackingRequest(Status: "PENDING_SIGN"));
        Assert.Equal([100L], byStatus.Items.Select(i => i.VisitId).ToArray());
        Assert.Equal(1, byStatus.TotalCount);
        Assert.Equal(2, byStatus.Stats.TotalVisits);

        var byKeyword = await _service.GetDiagnosticResultsTrackingAsync(
            3,
            new GetDiagnosticResultsTrackingRequest(Keyword: "PAT-0011"));
        Assert.Equal([101L], byKeyword.Items.Select(i => i.VisitId).ToArray());

        var paged = await _service.GetDiagnosticResultsTrackingAsync(
            3,
            new GetDiagnosticResultsTrackingRequest(Page: 2, PageSize: 1));
        Assert.Equal([100L], paged.Items.Select(i => i.VisitId).ToArray());
        Assert.Equal(2, paged.TotalCount);
        Assert.Equal(2, paged.TotalPages);

        var emptyDate = await _service.GetDiagnosticResultsTrackingAsync(
            3,
            new GetDiagnosticResultsTrackingRequest(Date: "2026-10-19"));
        Assert.Empty(emptyDate.Items);
        Assert.Equal(0, emptyDate.Stats.TotalVisits);
        Assert.Equal(0, emptyDate.TotalPages);
    }

    [Fact]
    public async Task InvalidInput_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticResultsTrackingAsync(
                3,
                new GetDiagnosticResultsTrackingRequest(Date: "20-10-2026")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticResultsTrackingAsync(
                3,
                new GetDiagnosticResultsTrackingRequest(Status: "ABNORMAL")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticResultsTrackingAsync(
                3,
                new GetDiagnosticResultsTrackingRequest(Keyword: "<script>")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticResultsTrackingAsync(
                3,
                new GetDiagnosticResultsTrackingRequest(Page: 0)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticResultsTrackingAsync(
                3,
                new GetDiagnosticResultsTrackingRequest(PageSize: 101)));
    }

    [Fact]
    public async Task Auth_RejectsAnonymousAndNonDoctor()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetDiagnosticResultsTrackingAsync(0));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticResultsTrackingAsync(7));
    }
}
