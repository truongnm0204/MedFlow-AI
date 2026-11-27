using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Realtime;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MedFlow.IntegrationTests.Visits;

[CollectionDefinition("ClinicalRecoverySerial", DisableParallelization = true)]
public sealed class ClinicalRecoverySerialCollection
{
}

/// <summary>
/// Commit A (PR #859 / Issue #858, F01 Option B) — Clinical Session Recovery Authority.
/// Covers RA-01..RA-26 against real PostgreSQL + Migration 028 fresh/populated/idempotent checks
/// and the RCX rollover regression (no auto-defer of active execution).
///
/// Tests depend on the seeded medflow_test fixture (hospital 1, users 1..14); every created row is
/// tagged with a unique suffix and removed in <c>finally</c>.
/// </summary>
[Collection("ClinicalRecoverySerial")]
public sealed class ClinicalSessionRecoveryAuthorityPostgresTests : AuthTestBase
{
    private readonly TestClock _clock = new(new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Unspecified));

    public ClinicalSessionRecoveryAuthorityPostgresTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    // =====================================================================================
    // RA-01 / RA-20: request lifecycle (no queue/visit mutation, one unresolved per queue)
    // =====================================================================================

    [Fact]
    public async Task RA01_RA20_ReceptionCreatesRequestWithoutMutation_AndSecondUnresolvedRequestIsRejected()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA01");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var beforeVisit = await db.Visits.AsNoTracking().SingleAsync(v => v.Id == fx.Visit1Id);
            var beforeQueue = await db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);

            var created = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Bệnh nhân quay lại, phiên kết luận trước chưa giải phóng."));

            Assert.Equal(ClinicalSessionRecoveryStatuses.Requested, created.Status);
            Assert.Equal(fx.Queue1Id, created.QueueEntryId);
            Assert.Equal(fx.DoctorAId, created.OriginalDoctorId);

            var afterVisit = await db.Visits.AsNoTracking().SingleAsync(v => v.Id == fx.Visit1Id);
            var afterQueue = await db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal(beforeVisit.Status, afterVisit.Status);
            Assert.Equal(beforeVisit.AssignedDoctorId, afterVisit.AssignedDoctorId);
            Assert.Equal(beforeQueue.Status, afterQueue.Status);
            Assert.Equal(beforeQueue.StateVersion, afterQueue.StateVersion);
            Assert.Null(afterQueue.CompletedAt);

            var duplicate = await Assert.ThrowsAsync<ConflictException>(() => svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu thứ hai cho cùng lượt hàng đợi.")));
            Assert.Contains("RECOVERY_REQUEST_ALREADY_UNRESOLVED", duplicate.Message, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-02 / RA-03 / RA-04 / RA-05: non-physician and insufficient-permission denial
    // =====================================================================================

    [Fact]
    public async Task RA02_RA03_RA04_RA05_NonPhysicianAndInsufficientAuthority_AreDeniedWithoutMutation()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA02");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu phục hồi phiên khám kết luận bị gián đoạn."));

            // RA-02: Reception cannot grant or confirm.
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.GrantAsync(
                fx.ReceptionUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Lễ tân thử cấp grant.")));
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.ConfirmAsync(
                fx.ReceptionUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Lễ tân thử xác nhận.")));

            Assert.Equal(ClinicalSessionRecoveryStatuses.Requested,
                (await db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId)).Status);

            // RA-03: PATIENT / DIAGNOSTIC_STAFF / PHARMACY_STAFF / SYSTEM_ADMIN cannot create requests.
            foreach (var userId in new[] { fx.PatientUserId, fx.DiagStaffUserId, fx.PharmacyStaffUserId, fx.SystemAdminUserId })
            {
                await Assert.ThrowsAsync<ForbiddenException>(() => svc.RequestRecoveryAsync(
                    userId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Vai trò không hợp lệ thử tạo yêu cầu.")));
            }

            // RA-04: HOSPITAL_ADMIN has no clinical grant authority by role.
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.GrantAsync(
                fx.HospitalAdminUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Quản trị bệnh viện thử cấp grant.")));
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.ConfirmAsync(
                fx.HospitalAdminUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Quản trị bệnh viện thử xác nhận.")));

            // RA-05: DOCTOR with RECOVER only (no GRANT) cannot issue a grant.
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.GrantAsync(
                fx.DoctorCUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Bác sĩ không có quyền cấp grant.")));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task RA02_RA03_EndpointRoleGate_RejectsNonClinicalRolesWith403()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RAH");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

            foreach (var (userId, email, role) in new[]
                     {
                         (fx.PatientUserId, fx.PatientEmail, RoleNames.Patient),
                         (fx.DiagStaffUserId, fx.DiagStaffEmail, RoleNames.DiagnosticStaff),
                         (fx.PharmacyStaffUserId, fx.PharmacyStaffEmail, RoleNames.PharmacyStaff),
                         (fx.SystemAdminUserId, fx.SystemAdminEmail, RoleNames.SystemAdmin),
                     })
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/api/v1/visits/{fx.Visit1Code}/clinical-session-recovery-context");
                req.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer", jwt.GenerateAccessToken(new User { Id = userId, Email = email }, new[] { role }).AccessToken);
                var res = await Client.SendAsync(req);
                Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            }
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-06 / RA-07: self-grant and ineligible recipient
    // =====================================================================================

    [Fact]
    public async Task RA06_RA07_SelfGrantRejected_AndRecipientWithoutRecoverPermissionRejected()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA06");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu phục hồi phiên bị gián đoạn."));

            var selfGrant = await Assert.ThrowsAsync<ConflictException>(() => svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorAId, 4, "Bác sĩ tự cấp grant cho mình.")));
            Assert.Contains("SELF_GRANT", selfGrant.Message, StringComparison.Ordinal);

            // doctor02@medflow.test: same hospital/active but no CLINICAL_SESSION_RECOVER.
            // D6 final: actor has visibility, target doctor lacks RECOVER → 403 (not 409/404).
            var ineligible = await Assert.ThrowsAsync<ForbiddenException>(() => svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.SeededDoctor2Id, 4, "Cấp grant cho bác sĩ thiếu quyền RECOVER.")));
            Assert.Contains("RECIPIENT_NOT_ELIGIBLE", ineligible.Message, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-08 / RA-24: hospital/clinical scope does not leak existence
    // =====================================================================================

    [Fact]
    public async Task RA08_RA24_CrossHospitalAccessReturnsUniform404_ForContextAndOperations()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA08");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var otherRequest = await svc.RequestRecoveryAsync(
                fx.DoctorDUserId, fx.Visit2Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue2Id, 2, "Yêu cầu tại bệnh viện khác."));

            // Doctor B (hospital 1) cannot read the other hospital's context.
            var contextEx = await Assert.ThrowsAsync<NotFoundException>(() =>
                svc.GetContextAsync(fx.DoctorBUserId, fx.Visit2Code));
            Assert.Contains("CLINICAL_SESSION_RECOVERY_NOT_FOUND", contextEx.Message, StringComparison.Ordinal);

            // ... nor operate on the request.
            var confirmEx = await Assert.ThrowsAsync<NotFoundException>(() => svc.ConfirmAsync(
                fx.DoctorBUserId, otherRequest.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(2, true, "Xác nhận ngoài phạm vi bệnh viện.")));
            Assert.Contains("CLINICAL_SESSION_RECOVERY_NOT_FOUND", confirmEx.Message, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-09 / RA-17 / RA-23: delegated grant + confirm mutation matrix
    // =====================================================================================

    [Fact]
    public async Task RA09_RA17_RA23_DelegatedGrantTwoHourExpiry_AndConfirmReleasesOnlyTheClinicalTicket()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA09");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Bác sĩ phụ trách nghỉ phép, cần bác sĩ khác đánh giá."));

            var queueCountBefore = await db.QueueEntries.CountAsync(q => q.VisitId == fx.Visit1Id);
            var paymentCountBefore = await db.Payments.CountAsync(p => p.VisitId == fx.Visit1Id);
            var presenceBefore = (await db.Visits.AsNoTracking().SingleAsync(v => v.Id == fx.Visit1Id)).PresenceState;

            var granted = await svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Cấp grant cho bác sĩ đủ chuyên môn đánh giá phiên."));

            Assert.Equal(ClinicalSessionRecoveryStatuses.Granted, granted.Status);
            Assert.Equal(fx.DoctorBId, granted.AuthorizedDoctorId);
            Assert.NotNull(granted.GrantedAt);
            Assert.NotNull(granted.ExpiresAt);
            Assert.Equal(TimeSpan.FromHours(2), granted.ExpiresAt!.Value - granted.GrantedAt!.Value);

            var confirm = await svc.ConfirmAsync(
                fx.DoctorBUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Đã xác minh phiên khám trước không còn được thực hiện trực tiếp."));

            Assert.False(confirm.IsIdempotentReplay);
            Assert.Equal(ClinicalSessionRecoveryStatuses.Recovered, confirm.Status);
            Assert.Equal("DEFERRED", confirm.QueueStatus);

            var visit = await db.Visits.AsNoTracking().SingleAsync(v => v.Id == fx.Visit1Id);
            var queue = await db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            var recovered = await db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);

            Assert.Equal("DEFERRED", queue.Status);
            Assert.NotNull(queue.CompletedAt);
            Assert.Equal(5, queue.StateVersion);
            Assert.StartsWith(ClinicalSessionRecoveryConstants.RecoveryQueueStatusReasonPrefix, queue.BlockedReason);
            Assert.Equal(fx.DoctorAId, visit.AssignedDoctorId); // never reassigned by recovery
            Assert.Equal(presenceBefore, visit.PresenceState);   // never stamps presence
            Assert.Equal(queueCountBefore, await db.QueueEntries.CountAsync(q => q.VisitId == fx.Visit1Id)); // no new ticket
            Assert.Equal(paymentCountBefore, await db.Payments.CountAsync(p => p.VisitId == fx.Visit1Id));  // no payment mutation
            Assert.Equal(ClinicalSessionRecoveryStatuses.Recovered, recovered.Status);
            Assert.Equal(fx.DoctorBId, recovered.RecoveredByDoctorId);
            Assert.False(recovered.OwnSession);
            Assert.False(string.IsNullOrWhiteSpace(recovered.RecoveryReason));
            Assert.True(await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.Action == ClinicalSessionRecoveryConstants.RecoveryConfirmedAuditAction &&
                a.EntityId == request.RequestId.ToString() &&
                a.ActorUserId == fx.DoctorBUserId));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-10: grant tuple binding (another queue/request cannot consume it)
    // =====================================================================================

    [Fact]
    public async Task RA10_GrantIsBoundToItsQueueAndVisit_NotReusableForAnotherSession()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA10");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var requestA = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu cho phiên thứ nhất."));
            await svc.GrantAsync(
                fx.DoctorAUserId, requestA.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Grant chỉ cho phiên thứ nhất."));

            // A second independent visit/queue/session for the same hospital.
            var (visit3Id, visit3Code, queue3Id) = await CreateClinicalSessionAsync(
                fx, "RA10B", fx.DoctorAId, "RETURN_CONSULTATION", "IN_PROGRESS", stateVersion: 1);
            fx.ExtraVisitIds.Add(visit3Id);
            fx.ExtraQueueIds.Add(queue3Id);

            var requestB = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, visit3Code,
                new CreateClinicalSessionRecoveryRequestDto(queue3Id, 1, "Yêu cầu cho phiên thứ hai."));

            var ex = await Assert.ThrowsAsync<ForbiddenException>(() => svc.ConfirmAsync(
                fx.DoctorBUserId, requestB.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(1, true, "Bác sĩ B thử dùng grant của phiên khác.")));
            Assert.Contains("NO_PHYSICIAN_AUTHORITY", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-11 / RA-12: expired and revoked grants cannot confirm
    // =====================================================================================

    [Fact]
    public async Task RA11_ExpiredGrantBlocksConfirm_TransitionsExpired_AndAllowsFreshRequest()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA11");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db, _clock);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu trước khi grant hết hạn."));
            await svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Grant sẽ hết hạn sau 2 giờ."));

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Unspecified));

            var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmAsync(
                fx.DoctorBUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Xác nhận sau khi grant đã hết hạn.")));
            Assert.Contains("RECOVERY_GRANT_EXPIRED", ex.Message, StringComparison.Ordinal);

            var expired = await db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
            Assert.Equal(ClinicalSessionRecoveryStatuses.Expired, expired.Status);
            Assert.NotNull(expired.ExpiredAt);

            // A fresh request is still possible after the expired grant (never permanently blocked).
            var fresh = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu mới sau grant hết hạn."));
            Assert.Equal(ClinicalSessionRecoveryStatuses.Requested, fresh.Status);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task RA12_RevokedGrantBlocksConfirm_AndCannotBeReactivated()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA12");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu sẽ bị thu hồi grant."));
            await svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Grant hợp lệ trước khi bị thu hồi."));

            var revoked = await svc.RevokeAsync(
                fx.DoctorAUserId, request.RequestId,
                new ClinicalSessionRecoveryDecisionRequestDto("Thu hồi grant do thay đổi ca trực.", 4));
            Assert.Equal(ClinicalSessionRecoveryStatuses.Revoked, revoked.Status);

            var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmAsync(
                fx.DoctorBUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Xác nhận sau khi grant bị thu hồi.")));
            Assert.Contains("RECOVERY_GRANT_NOT_ACTIVE", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-13: stale StateVersion / changed assignment
    // =====================================================================================

    [Fact]
    public async Task RA13_StaleStateVersionRejected_OnRequestAndGrant()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA13");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var stale = await Assert.ThrowsAsync<ConflictException>(() => svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 3, "StateVersion cũ từ màn hình khác.")));
            Assert.Contains("RECOVERY_STALE_STATE", stale.Message, StringComparison.Ordinal);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu với StateVersion hiện hành."));

            var staleGrant = await Assert.ThrowsAsync<ConflictException>(() => svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 3, "Grant với StateVersion cũ.")));
            Assert.Contains("RECOVERY_STALE_STATE", staleGrant.Message, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-14 / RA-22: not interrupted and non-clinical queue types
    // =====================================================================================

    [Fact]
    public async Task RA14_RA22_NonServingQueueAndClsPharmacyAreRejected_WithoutMutation()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA14");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var (waitingVisitId, waitingCode, waitingQueueId) = await CreateClinicalSessionAsync(
                fx, "RA14W", fx.DoctorAId, "RETURN_CONSULTATION", "WAITING", 1);
            fx.ExtraVisitIds.Add(waitingVisitId);
            fx.ExtraQueueIds.Add(waitingQueueId);

            var waitingEx = await Assert.ThrowsAsync<ConflictException>(() => svc.RequestRecoveryAsync(
                fx.ReceptionUserId, waitingCode,
                new CreateClinicalSessionRecoveryRequestDto(waitingQueueId, 1, "Vé chưa bắt đầu, không phải phiên gián đoạn.")));
            Assert.Contains("RECOVERY_SESSION_NOT_INTERRUPTED", waitingEx.Message, StringComparison.Ordinal);

            foreach (var queueType in new[] { "DIAGNOSTIC", "PHARMACY" })
            {
                var (vId, vCode, qId) = await CreateClinicalSessionAsync(
                    fx, $"RA14{queueType[..3]}", fx.DoctorAId, queueType, "IN_PROGRESS", 1);
                fx.ExtraVisitIds.Add(vId);
                fx.ExtraQueueIds.Add(qId);

                var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.RequestRecoveryAsync(
                    fx.ReceptionUserId, vCode,
                    new CreateClinicalSessionRecoveryRequestDto(qId, 1, "Không dùng clinical recovery để hủy CLS/Dược.")));
                Assert.Contains("RECOVERY_NOT_CLINICAL_QUEUE", ex.Message, StringComparison.Ordinal);

                Assert.Equal(0, await db.ClinicalSessionRecoveryRequests.CountAsync(r => r.QueueEntryId == qId));
            }
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-15 / RA-16: own-session vs non-assigned RECOVER doctor
    // =====================================================================================

    [Fact]
    public async Task RA15_RA16_AssignedDoctorRecoversOwnSession_NonAssignedDoctorWithoutGrantIsForbidden()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA15");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Phiên của bác sĩ phụ trách bị gián đoạn."));

            // RA-16: Doctor C has RECOVER but is not assigned and holds no grant.
            var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => svc.ConfirmAsync(
                fx.DoctorCUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Bác sĩ không phụ trách thử xác nhận.")));
            Assert.Contains("NO_PHYSICIAN_AUTHORITY", forbidden.Message, StringComparison.Ordinal);

            // RA-15: assigned doctor A (also RECOVER) confirms own session WITHOUT any delegated grant.
            var confirm = await svc.ConfirmAsync(
                fx.DoctorAUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Bác sĩ phụ trách xác nhận phiên của mình đã gián đoạn."));
            Assert.Equal(ClinicalSessionRecoveryStatuses.Recovered, confirm.Status);

            var recovered = await db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
            Assert.True(recovered.OwnSession);
            Assert.Null(recovered.AuthorizedDoctorId);
            Assert.Equal(fx.DoctorAId, recovered.RecoveredByDoctorId);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-18 / RA-19: idempotent replay (no duplicate release/audit) and payload mismatch
    // =====================================================================================

    [Fact]
    public async Task RA18_RA19_ConfirmReplayIsIdempotent_SameKeyDifferentPayloadIsRejected()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA18");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Phiên gián đoạn cần phục hồi."));

            var payload = new ConfirmClinicalSessionRecoveryRequestDto(
                4, true, "Đã xác minh phiên không còn thực hiện.", "recovery-request-" + fx.Suffix);

            var first = await svc.ConfirmAsync(fx.DoctorAUserId, request.RequestId, payload);
            Assert.False(first.IsIdempotentReplay);

            var replay = await svc.ConfirmAsync(fx.DoctorAUserId, request.RequestId, payload);
            Assert.True(replay.IsIdempotentReplay);
            Assert.Equal(5, replay.QueueStateVersion);

            var queue = await db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal(5, queue.StateVersion); // exactly one release
            Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
                a.Action == ClinicalSessionRecoveryConstants.RecoveryConfirmedAuditAction &&
                a.EntityId == request.RequestId.ToString()));

            var mismatch = await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmAsync(
                fx.DoctorAUserId, request.RequestId,
                payload with { Reason = "Payload khác với lần xác nhận trước." }));
            Assert.Contains("IDEMPOTENCY_MISMATCH", mismatch.Message, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-21 / RA-25 / RA-26: terminal Visit, no authority, terminal states never reactivated
    // =====================================================================================

    [Fact]
    public async Task RA21_RA25_RA26_TerminalVisitAndTerminalRecoveryStates_AreFailClosed()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA21");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var visit = await db.Visits.SingleAsync(v => v.Id == fx.Visit1Id);
            visit.Status = "COMPLETED";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var terminalVisit = await Assert.ThrowsAsync<ConflictException>(() => svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Lượt khám đã kết thúc.")));
            Assert.Contains("RECOVERY_VISIT_TERMINAL", terminalVisit.Message, StringComparison.Ordinal);

            await db.Visits.Where(v => v.Id == fx.Visit1Id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, "IN_PROGRESS"));

            // Rejected request cannot be granted afterwards.
            var r1 = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu sẽ bị từ chối."));
            await svc.RejectAsync(fx.DoctorAUserId, r1.RequestId,
                new ClinicalSessionRecoveryDecisionRequestDto("Không có căn cứ phiên gián đoạn.", 4));
            var rejectedGrant = await Assert.ThrowsAsync<ConflictException>(() => svc.GrantAsync(
                fx.DoctorAUserId, r1.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Cấp grant sau khi đã từ chối.")));
            Assert.Contains("GRANT_NOT_ACTIVE", rejectedGrant.Message, StringComparison.Ordinal);

            // Cancelled request cannot be granted.
            var r2 = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu sẽ bị rút."));
            await svc.CancelAsync(fx.ReceptionUserId, r2.RequestId,
                new ClinicalSessionRecoveryDecisionRequestDto("Rút yêu cầu trùng.", 4));
            var cancelledGrant = await Assert.ThrowsAsync<ConflictException>(() => svc.GrantAsync(
                fx.DoctorAUserId, r2.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Cấp grant sau khi đã rút.")));
            Assert.Contains("GRANT_NOT_ACTIVE", cancelledGrant.Message, StringComparison.Ordinal);

            // RA-25: without any grant the non-assigned doctor cannot confirm (no physician authority).
            var r3 = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Không có bác sĩ đủ thẩm quyền."));
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.ConfirmAsync(
                fx.DoctorCUserId, r3.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Không có thẩm quyền chuyên môn.")));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RA-25 (D6 edge): missing clinical mapping is unprovable visibility → uniform 404
    // =====================================================================================

    [Fact]
    public async Task RA25_MissingClinicalMapping_FailsClosedWithUniform404_Before403()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RA25");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            // Visit has no appointment/service and no examination room → department/specialty unprovable.
            var (visitId, visitCode, queueId) = await CreateClinicalSessionAsync(
                fx, "NOMAP", fx.DoctorAId, "RETURN_CONSULTATION", "IN_PROGRESS", 1,
                examinationRoomId: null);
            fx.ExtraVisitIds.Add(visitId);
            fx.ExtraQueueIds.Add(queueId);

            // Doctor A holds RECOVER+GRANT, but visibility cannot be proven → 404, not 403.
            await Assert.ThrowsAsync<NotFoundException>(() => svc.GetContextAsync(fx.DoctorAUserId, visitCode));
            await Assert.ThrowsAsync<NotFoundException>(() => svc.RequestRecoveryAsync(
                fx.DoctorAUserId, visitCode,
                new CreateClinicalSessionRecoveryRequestDto(queueId, 1, "Không xác định được phạm vi chuyên môn.")));

            // Reception (non-physician visibility) can create the request; a physician still cannot act on it.
            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, visitCode,
                new CreateClinicalSessionRecoveryRequestDto(queueId, 1, "Yêu cầu từ quầy tiếp đón."));
            await Assert.ThrowsAsync<NotFoundException>(() => svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 1, "Cấp grant khi chưa chứng minh được scope.")));
            await Assert.ThrowsAsync<NotFoundException>(() => svc.ConfirmAsync(
                fx.DoctorAUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(1, true, "Xác nhận khi chưa chứng minh được scope.")));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // F-01 (security): unresolvable visit hospital is not proof of tenancy → uniform 404
    // =====================================================================================

    [Fact]
    public async Task F01_UnresolvableVisitHospital_FailsClosedWithUniform404()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("F01");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);
            var nowLocal = _clock.GetHospitalNow();

            // Degenerate visit: no appointment, doctor, room, checked-in staff, queue service point or audit.
            var visit = new Visit
            {
                VisitCode = $"V{fx.Suffix[..6]}NOHOSP" + Guid.NewGuid().ToString("N")[..4],
                PatientId = null,
                AssignedDoctorId = null,
                ExaminationRoomId = null,
                CheckedInByStaffId = null,
                Status = "IN_PROGRESS",
                CheckedInAt = nowLocal,
                PresenceState = "PRESENT",
            };
            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            fx.ExtraVisitIds.Add(visit.Id);

            var queue = new QueueEntry
            {
                VisitId = visit.Id,
                QueueDate = DateOnly.FromDateTime(nowLocal.AddDays(-1)),
                QueueNumber = Random.Shared.Next(1, 9_000_000),
                QueueType = "RETURN_CONSULTATION",
                Status = "IN_PROGRESS",
                StateVersion = 1,
            };
            db.QueueEntries.Add(queue);
            await db.SaveChangesAsync();
            fx.ExtraQueueIds.Add(queue.Id);

            // Reception has no clinical-scope fallback: an unresolvable hospital must not bypass tenancy.
            await Assert.ThrowsAsync<NotFoundException>(() =>
                svc.GetContextAsync(fx.ReceptionUserId, visit.VisitCode!));
            await Assert.ThrowsAsync<NotFoundException>(() => svc.RequestRecoveryAsync(
                fx.ReceptionUserId, visit.VisitCode!,
                new CreateClinicalSessionRecoveryRequestDto(queue.Id, 1, "Thử vượt ràng buộc tenancy.")));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // PM-approved Commit B: scoped list + eligible recipients
    // =====================================================================================

    [Fact]
    public async Task CommitB_ListAndEligibleRecipients_AreActorScoped()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("CBL");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Yêu cầu cho danh sách theo scope."));

            // Reception and granting doctor see the hospital/clinical-scope request.
            var receptionList = await svc.ListRequestsAsync(fx.ReceptionUserId);
            Assert.Contains(receptionList, r => r.RequestId == request.RequestId);

            var doctorAList = await svc.ListRequestsAsync(fx.DoctorAUserId);
            Assert.Contains(doctorAList, r => r.RequestId == request.RequestId);

            // Other hospital's doctor sees only their own tenant (empty here).
            var doctorDList = await svc.ListRequestsAsync(fx.DoctorDUserId);
            Assert.DoesNotContain(doctorDList, r => r.RequestId == request.RequestId);

            // Patient / SystemAdmin role classes are forbidden, not an empty list.
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.ListRequestsAsync(fx.PatientUserId));
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.ListRequestsAsync(fx.SystemAdminUserId));

            // Eligible recipients: only active + RECOVER + same hospital/department/specialty, never self.
            var recipients = await svc.GetEligibleRecipientsAsync(fx.DoctorAUserId, request.RequestId);
            Assert.Contains(recipients, r => r.DoctorId == fx.DoctorBId);
            Assert.Contains(recipients, r => r.DoctorId == fx.DoctorCId);
            Assert.DoesNotContain(recipients, r => r.DoctorId == fx.DoctorAId);
            Assert.DoesNotContain(recipients, r => r.DoctorId == fx.SeededDoctor2Id); // no RECOVER, other department
            Assert.DoesNotContain(recipients, r => r.DoctorId == fx.DoctorDId);        // other hospital

            // Reception has visibility but no GRANT → 403; cross-hospital doctor has no visibility → 404.
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                svc.GetEligibleRecipientsAsync(fx.ReceptionUserId, request.RequestId));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                svc.GetEligibleRecipientsAsync(fx.DoctorDUserId, request.RequestId));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // D7 revise: reasons visible to scoped actors, never in errors/SignalR
    // =====================================================================================

    [Fact]
    public async Task D7_ReasonsVisibleToScopedActors_NotInErrorsOrSignalR()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("D7R");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var publisher = new RecordingRealtimePublisher();
            var svc = new ClinicalSessionRecoveryService(db, publisher, _clock);

            const string requestReason = "reason-request-7f3a";
            const string grantReason = "reason-grant-7f3b";
            const string recoveryReason = "reason-recovery-7f3c";

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, requestReason));
            Assert.Equal(requestReason, request.RequestReason);

            var receptionList = await svc.ListRequestsAsync(fx.ReceptionUserId);
            Assert.Contains(receptionList, r => r.RequestId == request.RequestId && r.RequestReason == requestReason);

            var granted = await svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, grantReason));
            Assert.Equal(requestReason, granted.RequestReason);
            Assert.Equal(grantReason, granted.GrantReason);

            var confirmed = await svc.ConfirmAsync(
                fx.DoctorBUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(4, true, recoveryReason));
            Assert.Equal(recoveryReason, confirmed.RecoveryReason);

            var doctorList = await svc.ListRequestsAsync(fx.DoctorBUserId);
            Assert.Contains(doctorList, r =>
                r.RequestId == request.RequestId &&
                r.RequestReason == requestReason &&
                r.GrantReason == grantReason &&
                r.RecoveryReason == recoveryReason);

            // SignalR payload carries IDs/status only — no reason text.
            var wire = JsonSerializer.Serialize(publisher.QueueChanged);
            Assert.DoesNotContain(requestReason, wire, StringComparison.Ordinal);
            Assert.DoesNotContain(grantReason, wire, StringComparison.Ordinal);
            Assert.DoesNotContain(recoveryReason, wire, StringComparison.Ordinal);

            // Out-of-scope actor: uniform 404, no reason in the error payload.
            var notFound = await Assert.ThrowsAsync<NotFoundException>(() =>
                svc.GetContextAsync(fx.DoctorDUserId, fx.Visit1Code));
            Assert.DoesNotContain(requestReason, notFound.Message, StringComparison.Ordinal);
            var otherHospitalList = await svc.ListRequestsAsync(fx.DoctorDUserId);
            Assert.DoesNotContain(otherHospitalList, r => r.RequestId == request.RequestId);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // F-02 (security): SYSTEM_ADMIN has no return-context bypass
    // =====================================================================================

    [Fact]
    public async Task F02_SystemAdminReturnCoordinationContext_Returns403()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("F02");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var returnSvc = scope.ServiceProvider.GetRequiredService<IReturnCoordinationService>();
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                returnSvc.GetReturnCoordinationContextAsync(fx.SystemAdminUserId, fx.Visit1Code));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task RCX07_RCX08_SystemAdminForbiddenOnLegacyResumeAliasesAndCanonicalReturnEnrollment()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCX7");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwt.GenerateAccessToken(
                new User { Id = fx.SystemAdminUserId, Email = fx.SystemAdminEmail },
                new[] { RoleNames.SystemAdmin }).AccessToken;

            foreach (var (method, url, body) in new (HttpMethod, string, object)[]
                     {
                         (HttpMethod.Post, $"/api/v1/visits/{fx.Visit1Code}/resume", new { target = "RETURN_CONSULTATION" }),
                         (HttpMethod.Post, $"/api/v1/reception/visits/{fx.Visit1Code}/resume", new { target = "RETURN_CONSULTATION" }),
                         (HttpMethod.Post, $"/api/v1/reception/visits/{fx.Visit1Code}/return-enrollment", new { }),
                         (HttpMethod.Post, $"/api/v1/visits/{fx.Visit1Code}/return-to-doctor", new { servicePointId = "1", confirmPresent = true, reason = "SYSTEM_ADMIN thử cấp vé RETURN" }),
                     })
            {
                using var req = new HttpRequestMessage(method, url)
                {
                    Content = JsonContent.Create(body),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var res = await Client.SendAsync(req);
                Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            }
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // §10.4 deterministic PostgreSQL races (independent scopes/connections + gate)
    // =====================================================================================

    [Fact]
    public async Task CommitB_Race_RequestVsRequest_OnlyOneUnresolvedRequest()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCQR");
        try
        {
            using var gate = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Exception?> Attempt()
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    await svc.RequestRecoveryAsync(
                        fx.ReceptionUserId, fx.Visit1Code,
                        new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Race request vs request."));
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var t1 = Task.Run(Attempt);
            var t2 = Task.Run(Attempt);
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            release.SetResult();
            var results = await Task.WhenAll(t1, t2);

            Assert.Equal(1, results.Count(r => r is null));
            Assert.All(results.Where(r => r is not null), r => Assert.IsType<ConflictException>(r));
            Db.ChangeTracker.Clear();
            Assert.Equal(1, await Db.ClinicalSessionRecoveryRequests.CountAsync(r =>
                r.QueueEntryId == fx.Queue1Id &&
                (r.Status == ClinicalSessionRecoveryStatuses.Requested || r.Status == ClinicalSessionRecoveryStatuses.Granted)));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_Race_GrantVsGrant_OnlyOneEffectiveGrant()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCQG");
        try
        {
            long requestId;
            using (var seedScope = Factory.Services.CreateScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var seedSvc = NewService(seedDb);
                var request = await seedSvc.RequestRecoveryAsync(
                    fx.ReceptionUserId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed request for grant race."));
                requestId = request.RequestId;
            }

            using var gate = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Exception?> Attempt(long recipientId)
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    await svc.GrantAsync(
                        fx.DoctorAUserId, requestId,
                        new GrantClinicalSessionRecoveryRequestDto(recipientId, 4, "Race grant vs grant."));
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var t1 = Task.Run(() => Attempt(fx.DoctorBId));
            var t2 = Task.Run(() => Attempt(fx.DoctorCId));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            release.SetResult();
            var results = await Task.WhenAll(t1, t2);

            Assert.Equal(1, results.Count(r => r is null));
            Assert.All(results.Where(r => r is not null), r => Assert.IsType<ConflictException>(r));
            Db.ChangeTracker.Clear();
            var grants = await Db.ClinicalSessionRecoveryRequests
                .Where(r => r.QueueEntryId == fx.Queue1Id && r.Status == ClinicalSessionRecoveryStatuses.Granted)
                .ToListAsync();
            Assert.Single(grants);
            Assert.Contains(grants[0].AuthorizedDoctorId, new long?[] { fx.DoctorBId, fx.DoctorCId });
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_Race_ConfirmVsConfirm_ConsumesReleasesAuditsExactlyOnce()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCQC");
        try
        {
            long requestId;
            using (var seedScope = Factory.Services.CreateScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var seedSvc = NewService(seedDb);
                var request = await seedSvc.RequestRecoveryAsync(
                    fx.ReceptionUserId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed request for confirm race."));
                requestId = request.RequestId;
                await seedSvc.GrantAsync(
                    fx.DoctorAUserId, requestId,
                    new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Seed grant for confirm race."));
            }

            using var gate = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<ClinicalSessionRecoveryConfirmResponseDto?> Attempt()
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    return await svc.ConfirmAsync(
                        fx.DoctorBUserId, requestId,
                        new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Race confirm vs confirm."));
                }
                catch (ConflictException)
                {
                    return null;
                }
            }

            var t1 = Task.Run(Attempt);
            var t2 = Task.Run(Attempt);
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            release.SetResult();
            var responses = (await Task.WhenAll(t1, t2)).Where(r => r is not null).ToList();

            Assert.Equal(1, responses.Count(r => r!.IsIdempotentReplay == false));
            Assert.All(responses, r => Assert.Equal(ClinicalSessionRecoveryStatuses.Recovered, r!.Status));
            Db.ChangeTracker.Clear();
            var queue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal("DEFERRED", queue.Status);
            Assert.Equal(5, queue.StateVersion);
            Assert.Equal(1, await Db.AuditLogs.CountAsync(a =>
                a.Action == ClinicalSessionRecoveryConstants.RecoveryConfirmedAuditAction &&
                a.EntityId == requestId.ToString()));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_Race_ConfirmVsRevoke_OutcomeFollowsCommitOrder_NoPartialRelease()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCQV");
        try
        {
            long requestId;
            using (var seedScope = Factory.Services.CreateScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var seedSvc = NewService(seedDb);
                var request = await seedSvc.RequestRecoveryAsync(
                    fx.ReceptionUserId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed request for confirm/revoke race."));
                requestId = request.RequestId;
                await seedSvc.GrantAsync(
                    fx.DoctorAUserId, requestId,
                    new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Seed grant for confirm/revoke race."));
            }

            using var gate = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<string> Attempt(bool confirm)
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    if (confirm)
                    {
                        await svc.ConfirmAsync(
                            fx.DoctorBUserId, requestId,
                            new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Race confirm vs revoke."));
                        return "CONFIRM";
                    }

                    await svc.RevokeAsync(
                        fx.DoctorAUserId, requestId,
                        new ClinicalSessionRecoveryDecisionRequestDto("Race revoke vs confirm.", 4));
                    return "REVOKE";
                }
                catch (ConflictException)
                {
                    return "CONFLICT";
                }
            }

            var t1 = Task.Run(() => Attempt(true));
            var t2 = Task.Run(() => Attempt(false));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            release.SetResult();
            var results = await Task.WhenAll(t1, t2);

            Assert.Equal(1, results.Count(r => r is "CONFIRM" or "REVOKE"));

            Db.ChangeTracker.Clear();
            var requestRow = await Db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            var queue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            if (requestRow.Status == ClinicalSessionRecoveryStatuses.Recovered)
            {
                Assert.Equal("DEFERRED", queue.Status);
            }
            else
            {
                Assert.Equal(ClinicalSessionRecoveryStatuses.Revoked, requestRow.Status);
                Assert.Equal("IN_PROGRESS", queue.Status);
                Assert.Null(queue.CompletedAt);
            }
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_RollbackDoesNotPublishSuccessSignalR()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCQS");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var publisher = new RecordingRealtimePublisher();
            var svc = new ClinicalSessionRecoveryService(db, publisher, _clock);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed for rollback/SignalR test."));
            publisher.QueueChanged.Clear();

            // Stale state version → rollback before commit; no success invalidation may be published.
            await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmAsync(
                fx.DoctorAUserId, request.RequestId,
                new ConfirmClinicalSessionRecoveryRequestDto(99, true, "Thử rollback do sai state version.")));

            Assert.Empty(publisher.QueueChanged);
            Db.ChangeTracker.Clear();
            var queue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal("IN_PROGRESS", queue.Status);
            Assert.Null(queue.CompletedAt);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_Race_ConfirmVsResume_NeverCreatesTwoActiveTickets()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCQX");
        try
        {
            long requestId;
            using (var seedScope = Factory.Services.CreateScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var seedSvc = NewService(seedDb);
                var request = await seedSvc.RequestRecoveryAsync(
                    fx.ReceptionUserId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed for confirm/resume race."));
                requestId = request.RequestId;
                await seedSvc.GrantAsync(
                    fx.DoctorAUserId, requestId,
                    new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Seed grant for confirm/resume race."));
                await seedDb.Visits.Where(v => v.Id == fx.Visit1Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.ReadyForReturnAt, _clock.GetHospitalNow()));
            }

            using var gate = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<string> ConfirmAttempt()
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    await svc.ConfirmAsync(
                        fx.DoctorBUserId, requestId,
                        new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Race confirm vs resume."));
                    return "CONFIRM";
                }
                catch (ConflictException)
                {
                    return "CONFLICT";
                }
            }

            async Task<string> ResumeAttempt()
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var settlement = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
                var returnSvc = scope.ServiceProvider.GetRequiredService<IReturnCoordinationService>();
                var svc = new VisitCrossDayLifecycleService(
                    db, settlement, new NullRealtimeNotificationPublisher(), _clock, returnSvc);
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    await svc.ResumeVisitAsync(
                        fx.ReceptionUserId, fx.Visit1Code,
                        new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION", ConfirmPresent: true));
                    return "RESUME";
                }
                catch (ConflictException)
                {
                    return "CONFLICT";
                }
            }

            var t1 = Task.Run(ConfirmAttempt);
            var t2 = Task.Run(ResumeAttempt);
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            release.SetResult();
            var results = await Task.WhenAll(t1, t2);
            Assert.Contains("CONFIRM", results);

            Db.ChangeTracker.Clear();
            var requestRow = await Db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            Assert.Equal(ClinicalSessionRecoveryStatuses.Recovered, requestRow.Status);
            var oldQueue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal("DEFERRED", oldQueue.Status);

            var activeClinicalTickets = await Db.QueueEntries.AsNoTracking()
                .Where(q =>
                    q.VisitId == fx.Visit1Id &&
                    q.CompletedAt == null &&
                    (q.Status == "WAITING" || q.Status == "READY" || q.Status == "CALLED" ||
                     q.Status == "IN_PROGRESS" || q.Status == "IN_CONSULTATION" || q.Status == "SERVING"))
                .CountAsync();
            Assert.InRange(activeClinicalTickets, 0, 1);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_PermissionRevocationAtMutationPoint_BlocksConfirm()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCXPR");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var svc = NewService(db);

            var request = await svc.RequestRecoveryAsync(
                fx.ReceptionUserId, fx.Visit1Code,
                new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed for permission revocation."));
            await svc.GrantAsync(
                fx.DoctorAUserId, request.RequestId,
                new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Grant before permission revocation."));

            var recoverRoleId = await Db.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == fx.DoctorBUserId)
                .Join(Db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.RoleId, r.Code })
                .Where(x => x.Code.StartsWith("CSRR"))
                .Select(x => x.RoleId)
                .SingleAsync();
            await Db.UserRoles.Where(ur => ur.UserId == fx.DoctorBUserId && ur.RoleId == recoverRoleId)
                .ExecuteDeleteAsync();
            try
            {
                await Assert.ThrowsAsync<ForbiddenException>(() => svc.ConfirmAsync(
                    fx.DoctorBUserId, request.RequestId,
                    new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Attempt after RECOVER revocation.")));
            }
            finally
            {
                Db.ChangeTracker.Clear();
                Db.UserRoles.Add(new UserRole { UserId = fx.DoctorBUserId, RoleId = recoverRoleId });
                await Db.SaveChangesAsync();
            }

            Db.ChangeTracker.Clear();
            var queue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal("IN_PROGRESS", queue.Status);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_ExpireVsGrantAndConfirm_NoDuplicateLiveGrantOrConsume()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCXEX");
        try
        {
            long requestId;
            using (var seedScope = Factory.Services.CreateScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var seedSvc = NewService(seedDb);
                var request = await seedSvc.RequestRecoveryAsync(
                    fx.ReceptionUserId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed for expiry race."));
                requestId = request.RequestId;
                await seedSvc.GrantAsync(
                    fx.DoctorAUserId, requestId,
                    new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Grant for expiry race."));
            }

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Unspecified));

            using var gate = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<string> Attempt(bool confirm)
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    if (confirm)
                    {
                        await svc.ConfirmAsync(
                            fx.DoctorBUserId, requestId,
                            new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Confirm after expiry."));
                        return "CONFIRM";
                    }

                    await svc.GrantAsync(
                        fx.DoctorAUserId, requestId,
                        new GrantClinicalSessionRecoveryRequestDto(fx.DoctorCId, 4, "Re-grant after expiry."));
                    return "GRANT";
                }
                catch (ConflictException)
                {
                    return "CONFLICT";
                }
            }

            var t1 = Task.Run(() => Attempt(true));
            var t2 = Task.Run(() => Attempt(false));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            release.SetResult();
            await Task.WhenAll(t1, t2);

            Db.ChangeTracker.Clear();
            var row = await Db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            Assert.NotEqual(ClinicalSessionRecoveryStatuses.Recovered, row.Status);
            Assert.Equal(0, await Db.ClinicalSessionRecoveryRequests.CountAsync(r =>
                r.QueueEntryId == fx.Queue1Id && r.Status == ClinicalSessionRecoveryStatuses.Granted));
            var queue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal("IN_PROGRESS", queue.Status);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_Race_ConfirmVsCompleteVisit_NoRecoveryOfTerminalVisit()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCXT");
        try
        {
            long requestId;
            using (var seedScope = Factory.Services.CreateScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var seedSvc = NewService(seedDb);
                var request = await seedSvc.RequestRecoveryAsync(
                    fx.ReceptionUserId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed for confirm/complete race."));
                requestId = request.RequestId;
                await seedSvc.GrantAsync(
                    fx.DoctorAUserId, requestId,
                    new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Seed grant for confirm/complete race."));
            }

            using var lockScope = Factory.Services.CreateScope();
            var lockDb = lockScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            await using var lockTx = await lockDb.Database.BeginTransactionAsync();
            await lockDb.Visits.Where(v => v.Id == fx.Visit1Id).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.Status, "COMPLETED")
                .SetProperty(v => v.CompletedAt, _clock.GetHospitalNow()));

            using var started = new SemaphoreSlim(0, 1);
            var confirmTask = Task.Run(async () =>
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                started.Release();
                try
                {
                    await svc.ConfirmAsync(
                        fx.DoctorBUserId, requestId,
                        new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Confirm after visit completed."));
                    return "CONFIRM";
                }
                catch (ConflictException)
                {
                    return "CONFLICT";
                }
            });
            await started.WaitAsync(TimeSpan.FromSeconds(10));
            await lockTx.CommitAsync();
            var outcome = await confirmTask;
            await lockTx.DisposeAsync().AsTask();

            Assert.Equal("CONFLICT", outcome);
            Db.ChangeTracker.Clear();
            var row = await Db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            Assert.Equal(ClinicalSessionRecoveryStatuses.Granted, row.Status);
            var queue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal("IN_PROGRESS", queue.Status);
            Assert.Null(queue.CompletedAt);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_Race_ConfirmVsReassignment_GrantNotUsableAfterAssignmentChange()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCXA");
        try
        {
            long requestId;
            using (var seedScope = Factory.Services.CreateScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var seedSvc = NewService(seedDb);
                var request = await seedSvc.RequestRecoveryAsync(
                    fx.ReceptionUserId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed for confirm/reassign race."));
                requestId = request.RequestId;
                await seedSvc.GrantAsync(
                    fx.DoctorAUserId, requestId,
                    new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Seed grant for confirm/reassign race."));
            }

            using var lockScope = Factory.Services.CreateScope();
            var lockDb = lockScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            await using var lockTx = await lockDb.Database.BeginTransactionAsync();
            await lockDb.Visits.Where(v => v.Id == fx.Visit1Id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.AssignedDoctorId, fx.DoctorCId));

            using var started = new SemaphoreSlim(0, 1);
            var confirmTask = Task.Run(async () =>
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                started.Release();
                try
                {
                    await svc.ConfirmAsync(
                        fx.DoctorBUserId, requestId,
                        new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Confirm after reassignment."));
                    return "CONFIRM";
                }
                catch (ConflictException)
                {
                    return "CONFLICT";
                }
            });
            await started.WaitAsync(TimeSpan.FromSeconds(10));
            await lockTx.CommitAsync();
            var outcome = await confirmTask;
            await lockTx.DisposeAsync().AsTask();

            Assert.Equal("CONFLICT", outcome);
            Db.ChangeTracker.Clear();
            var row = await Db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            Assert.Equal(ClinicalSessionRecoveryStatuses.Granted, row.Status);
            var queue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            Assert.Equal("IN_PROGRESS", queue.Status);
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [Fact]
    public async Task CommitB_Race_ConfirmVsClinicalWrite_StaleWriterBlockedAfterRecovery()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCXW");
        try
        {
            long requestId;
            using (var seedScope = Factory.Services.CreateScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var seedSvc = NewService(seedDb);
                var request = await seedSvc.RequestRecoveryAsync(
                    fx.ReceptionUserId, fx.Visit1Code,
                    new CreateClinicalSessionRecoveryRequestDto(fx.Queue1Id, 4, "Seed for confirm/clinical-write race."));
                requestId = request.RequestId;
                await seedSvc.GrantAsync(
                    fx.DoctorAUserId, requestId,
                    new GrantClinicalSessionRecoveryRequestDto(fx.DoctorBId, 4, "Seed grant for clinical-write race."));
            }

            // Baseline: a clinical write on the still-open session must succeed before any recovery,
            // proving the concurrent write below is a real write path (fixture wiring/permissions valid).
            var baselineFindings = $"Baseline trước recovery {Guid.NewGuid():N}";
            using (var baselineScope = Factory.Services.CreateScope())
            {
                var baselineConsult = baselineScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await baselineConsult.RecordClinicalExaminationAsync(
                    fx.DoctorAUserId,
                    fx.Visit1Code,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: baselineFindings,
                        TreatmentPlan: "Theo dõi baseline."));
            }

            using var gate = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<string> ConfirmAttempt()
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var svc = NewService(db);
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    await svc.ConfirmAsync(
                        fx.DoctorBUserId, requestId,
                        new ConfirmClinicalSessionRecoveryRequestDto(4, true, "Race confirm vs clinical write."));
                    return "CONFIRM";
                }
                catch (ConflictException)
                {
                    return "CONFLICT";
                }
            }

            async Task<string> WriteAttempt()
            {
                using var scope = Factory.Services.CreateScope();
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                gate.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                try
                {
                    await consultSvc.RecordClinicalExaminationAsync(
                        fx.DoctorAUserId,
                        fx.Visit1Code,
                        new RecordClinicalExaminationRequest(
                            ClinicalFindings: "Ghi nhận lâm sàng chạy song song recovery.",
                            TreatmentPlan: "Theo dõi."));
                    return "WRITE";
                }
                catch (ConflictException)
                {
                    return "CONFLICT";
                }
                catch (ForbiddenException)
                {
                    return "FORBIDDEN";
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.GetType().Name;
                }
            }

            var t1 = Task.Run(ConfirmAttempt);
            var t2 = Task.Run(WriteAttempt);
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            await gate.WaitAsync(TimeSpan.FromSeconds(15));
            release.SetResult();
            var results = await Task.WhenAll(t1, t2);

            // The recovery confirm must always win deterministically: it never conflicts on its own,
            // so the clinical write either loses (CONFLICT) or lands before the release is visible.
            Assert.Contains("CONFIRM", results);
            Assert.DoesNotContain(results, r => r.StartsWith("ERROR:", StringComparison.Ordinal));

            Db.ChangeTracker.Clear();
            var row = await Db.ClinicalSessionRecoveryRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            Assert.Equal(ClinicalSessionRecoveryStatuses.Recovered, row.Status);

            // Captured BEFORE cleanup so assertions survive the fixture teardown deleting rows.
            var recoveredQueue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == fx.Queue1Id);
            var queueStatus = recoveredQueue.Status;
            var queueCompletedAt = recoveredQueue.CompletedAt;
            var recoveredVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == fx.Visit1Id);
            var visitStatus = AppointmentBookingRules.NormalizeStatusKey(recoveredVisit.Status);
            var raceRevisions = await Db.MedicalRecordRevisions.AsNoTracking()
                .Where(r => r.ClinicalFindings != null &&
                            r.ClinicalFindings.Contains("song song recovery") &&
                            Db.MedicalRecords.Any(m => m.Id == r.MedicalRecordId && m.VisitId == fx.Visit1Id))
                .CountAsync();
            var visit1Revisions = await Db.MedicalRecordRevisions.AsNoTracking()
                .Where(r => Db.MedicalRecords.Any(m => m.Id == r.MedicalRecordId && m.VisitId == fx.Visit1Id))
                .CountAsync();

            Assert.Equal("DEFERRED", queueStatus);
            Assert.NotNull(queueCompletedAt);

            // §10.4: no stale write trái phép — the post-recovery state machine never shows the
            // old session as still serving.
            Assert.True(
                visitStatus is "CHECKED_IN" or "AWAITING_RESULTS",
                $"Visit status sau recovery phải là non-serving (CHECKED_IN/AWAITING_RESULTS), thực tế: {visitStatus}.");

            // No lost update: any clinical write that reported success must be fully persisted
            // (revision visible after the race), never half-applied.
            if (results.Contains("WRITE"))
            {
                Assert.True(raceRevisions >= 1, "WRITE committed nhưng revision không tồn tại — lost update.");
                Assert.True(visit1Revisions >= 2, "Baseline + concurrent write đều phải persist đầy đủ.");
            }

            // Stale writer after recovery must not be allowed to continue as if the session were open.
            using var postScope = Factory.Services.CreateScope();
            var postConsult = postScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
            await Assert.ThrowsAsync<ConflictException>(() => postConsult.RecordClinicalExaminationAsync(
                fx.DoctorAUserId,
                fx.Visit1Code,
                new RecordClinicalExaminationRequest(
                    ClinicalFindings: "Ghi nhận sau recovery (phải bị chặn).",
                    TreatmentPlan: "Không hợp lệ.")));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    private sealed class RecordingRealtimePublisher : IRealtimeNotificationPublisher
    {
        public List<QueueChangedNotification> QueueChanged { get; } = [];

        public Task PublishQueueChangedAsync(QueueChangedNotification notification, CancellationToken ct = default)
        {
            QueueChanged.Add(notification);
            return Task.CompletedTask;
        }

        public Task PublishVisitPaymentChangedAsync(VisitPaymentChangedNotification notification, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // =====================================================================================
    // RCX-09 / RCX-10: HOSPITAL_ADMIN return-coordination exception policy
    // =====================================================================================

    [Fact]
    public async Task RCX09_RCX10_HospitalAdminPolicyOffOrMissingPermissionForbidden_PolicyOnWithBothPermissionsSucceedsWithAudit()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCX9");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var returnSvc = scope.ServiceProvider.GetRequiredService<IReturnCoordinationService>();

            var (v1Id, v1Code, sp1Id) = await CreateReadyReturnVisitAsync(fx, "OFF1");
            var (v2Id, v2Code, sp2Id) = await CreateReadyReturnVisitAsync(fx, "ON2");
            var (v3Id, v3Code, sp3Id) = await CreateReadyReturnVisitAsync(fx, "NOPERM3");

            // RCX-09a: policy flag off (default) → 403 even though HOSPITAL_ADMIN holds both permissions.
            var policyOff = await Assert.ThrowsAsync<ForbiddenException>(() => returnSvc.EnrollReturnConsultationAsync(
                fx.HospitalAdminUserId, v1Code,
                new CreateReturnEnrollmentRequestDto(
                    DoctorId: fx.DoctorAId, RoomId: 1, ServicePointId: sp1Id,
                    Purpose: QueueCoordinationPurposes.ResultConclusion, ConfirmPresent: true)));
            Assert.Contains("RETURN_ENROLLMENT_RECEPTION_REQUIRED", policyOff.Message, StringComparison.Ordinal);

            // Policy on; HOSPITAL_ADMIN with live QUEUE_ENROLL + VISIT_ASSIGN → allowed + exception audit.
            await EnableHospitalAdminReturnCoordinationAsync(fx, true);
            var success = await returnSvc.EnrollReturnConsultationAsync(
                fx.HospitalAdminUserId, v2Code,
                new CreateReturnEnrollmentRequestDto(
                    DoctorId: fx.DoctorAId, RoomId: 1, ServicePointId: sp2Id,
                    Purpose: QueueCoordinationPurposes.ResultConclusion, ConfirmPresent: true));
            Assert.Equal(v2Id, success.VisitId);
            Db.ChangeTracker.Clear();
            Assert.True(await Db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.Action == ReturnCoordinationService.HospitalAdminExceptionAuditAction &&
                a.ActorUserId == fx.HospitalAdminUserId &&
                a.EntityId == v2Code));

            // RCX-09b: policy on but HOSPITAL_ADMIN role temporarily lacks VISIT_ASSIGN → 403.
            var adminRoleId = await Db.Roles.AsNoTracking()
                .Where(r => r.Code == RoleNames.HospitalAdmin)
                .Select(r => r.Id)
                .SingleAsync();
            var visitAssignPermissionId = await Db.Permissions.AsNoTracking()
                .Where(p => p.Code == PermissionCodes.VisitAssign)
                .Select(p => p.Id)
                .SingleAsync();
            var removedMapping = await Db.RolePermissions
                .Where(rp => rp.RoleId == adminRoleId && rp.PermissionId == visitAssignPermissionId)
                .ToListAsync();
            Db.RolePermissions.RemoveRange(removedMapping);
            await Db.SaveChangesAsync();
            try
            {
                var missingPerm = await Assert.ThrowsAsync<ForbiddenException>(() => returnSvc.EnrollReturnConsultationAsync(
                    fx.HospitalAdminUserId, v3Code,
                    new CreateReturnEnrollmentRequestDto(
                        DoctorId: fx.DoctorAId, RoomId: 1, ServicePointId: sp3Id,
                        Purpose: QueueCoordinationPurposes.ResultConclusion, ConfirmPresent: true)));
                Assert.Contains("RETURN_ENROLLMENT_RECEPTION_REQUIRED", missingPerm.Message, StringComparison.Ordinal);
            }
            finally
            {
                foreach (var mapping in removedMapping)
                {
                    Db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = adminRoleId,
                        PermissionId = visitAssignPermissionId,
                    });
                }

                await Db.SaveChangesAsync();
            }
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // RCX-02..RCX-05 + RCX-08: rollover never auto-defers active execution; SYSTEM_ADMIN 403
    // =====================================================================================

    [Fact]
    public async Task RCX_RolloverNeverAutoDefersActiveExecution_AndSystemAdminIsForbidden()
    {
        if (!await CanConnectAsync()) return;
        var fx = await CreateFixtureAsync("RCX");
        try
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var settlement = new VisitChargeSettlementReader(db, _clock);
            var returnCoordination = scope.ServiceProvider.GetRequiredService<IReturnCoordinationService>();
            var service = new VisitCrossDayLifecycleService(
                db, settlement, new NullRealtimeNotificationPublisher(), _clock, returnCoordination);

            foreach (var (queueType, expectedCodes) in new[]
                     {
                         ("EXAMINATION", new[] { "CLINICAL_HANDOFF_REQUIRED" }),
                         ("RETURN_CONSULTATION", new[] { "CLINICAL_HANDOFF_REQUIRED" }),
                         ("DIAGNOSTIC", new[] { "CLINICAL_HANDOFF_REQUIRED", "ACTIVE_QUEUE_ALREADY_EXISTS", "CLINICAL_EXECUTION_IN_PROGRESS", "RETURN_CONSULTATION_NOT_READY" }),
                         ("PHARMACY", new[] { "CLINICAL_HANDOFF_REQUIRED", "ACTIVE_QUEUE_ALREADY_EXISTS", "CLINICAL_EXECUTION_IN_PROGRESS", "RETURN_CONSULTATION_NOT_READY" }),
                     })
            {
                var (visitId, visitCode, queueId) = await CreateClinicalSessionAsync(
                    fx, $"RCX{queueType[..3]}", fx.DoctorAId, queueType, "IN_PROGRESS", 1);
                fx.ExtraVisitIds.Add(visitId);
                fx.ExtraQueueIds.Add(queueId);

                var ex = await Assert.ThrowsAsync<ConflictException>(() => service.ResumeVisitAsync(
                    fx.ReceptionUserId, visitCode,
                    new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION", ConfirmPresent: true)));
                Assert.True(
                    expectedCodes.Any(code => ex.Message.Contains(code, StringComparison.Ordinal)),
                    $"Expected one of [{string.Join(", ", expectedCodes)}] but got: {ex.Message}");

                var queue = await db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == queueId);
                Assert.Equal("IN_PROGRESS", queue.Status);
                Assert.Null(queue.CompletedAt);
                Assert.Equal(1, queue.StateVersion);
            }

            // RCX-08: SYSTEM_ADMIN cannot resume or defer through this service.
            await Assert.ThrowsAsync<ForbiddenException>(() => service.ResumeVisitAsync(
                fx.SystemAdminUserId, fx.Visit1Code,
                new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION", ConfirmPresent: true)));
            await Assert.ThrowsAsync<ForbiddenException>(() => service.DeferVisitOrOrdersAsync(
                fx.SystemAdminUserId, fx.Visit1Code,
                new DeferVisitOrOrdersRequestDto("SYSTEM_ADMIN thử hoãn lượt khám.")));
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    // =====================================================================================
    // Migration 028: fresh DB, preflight, idempotent re-apply, populated DB, constraints
    // =====================================================================================

    [Fact]
    public async Task Migration028_FreshAndPopulatedApplyIsIdempotent_PreflightPasses_AndConstraintsHold()
    {
        if (!await CanConnectAsync()) return;

        var repoRoot = ResolveRepoRoot();
        var migration028 = await File.ReadAllTextAsync(
            Path.Combine(repoRoot, "database", "migrations", "028_clinical_session_recovery_authority.sql"));
        var preflight028 = await File.ReadAllTextAsync(
            Path.Combine(repoRoot, "database", "scripts", "preflight_028_audit.sql"));

        await using var tempDb = await TempDatabase.CreateAsync(ResolveAdminConnectionString());
        await ApplyMigrationsUpTo027Async(tempDb.ConnectionString, repoRoot);

        // Preflight on the populated pre-028 DB: permission checks fail by design (catalog not yet seeded).
        var preRows = await RunPreflightAsync(tempDb.ConnectionString, preflight028);
        Assert.Equal(3, preRows.Count);
        Assert.Contains(preRows, r => r.Check == "permissions_catalog_present" && r.Violations > 0);

        await ExecuteSqlAsync(tempDb.ConnectionString, migration028);
        await ExecuteSqlAsync(tempDb.ConnectionString, migration028); // re-apply (idempotent)

        var postRows = await RunPreflightAsync(tempDb.ConnectionString, preflight028);
        Assert.Equal(3, postRows.Count);
        Assert.All(postRows, r => Assert.Equal(0, r.Violations));
        Assert.All(postRows, r => Assert.Equal("PASS", r.Status));

        // Populated DB: insert one valid REQUESTED row from seeded data.
        const string insertRequested = """
            INSERT INTO clinical_session_recovery_requests
              (hospital_id, visit_id, queue_entry_id, requested_by_user_id, requested_at, request_reason,
               expected_queue_state_version, status, own_session, created_at, updated_at)
            SELECT h.id, v.id, q.id, u.id, now()::timestamp, 'Seed request', 1, 'REQUESTED', false,
                   now()::timestamp, now()::timestamp
            FROM hospitals h
            CROSS JOIN users u
            CROSS JOIN LATERAL (
              SELECT vv.id, qq.id AS queue_id
              FROM visits vv
              JOIN queue_entries qq ON qq.visit_id = vv.id
              ORDER BY vv.id, qq.id
              LIMIT 1
            ) v
            JOIN queue_entries q ON q.id = v.queue_id
            LIMIT 1;
            """;
        await ExecuteSqlAsync(tempDb.ConnectionString, insertRequested);
        await ExecuteSqlAsync(tempDb.ConnectionString, migration028); // populated re-apply

        // One unresolved request per QueueEntry (23505).
        const string duplicateUnresolved = """
            INSERT INTO clinical_session_recovery_requests
              (hospital_id, visit_id, queue_entry_id, requested_by_user_id, requested_at, request_reason,
               expected_queue_state_version, status, own_session, created_at, updated_at)
            SELECT hospital_id, visit_id, queue_entry_id, requested_by_user_id, now()::timestamp, 'Duplicate', 1,
                   'REQUESTED', false, now()::timestamp, now()::timestamp
            FROM clinical_session_recovery_requests
            LIMIT 1;
            """;
        var dupEx = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(tempDb.ConnectionString, duplicateUnresolved));
        Assert.Equal("23505", dupEx.SqlState);

        await ExecuteSqlAsync(tempDb.ConnectionString,
            "DELETE FROM clinical_session_recovery_requests WHERE request_reason IN ('Seed request', 'Duplicate');");

        // GRANTED without recipient/issuer/expiry/reason is rejected by chk_csrr_status_metadata (23514).
        const string invalidGranted = """
            INSERT INTO clinical_session_recovery_requests
              (hospital_id, visit_id, queue_entry_id, requested_by_user_id, requested_at, request_reason,
               expected_queue_state_version, status, own_session, created_at, updated_at)
            SELECT h.id, v.id, q.id, u.id, now()::timestamp, 'Invalid granted', 1, 'GRANTED', false,
                   now()::timestamp, now()::timestamp
            FROM hospitals h
            CROSS JOIN users u
            CROSS JOIN LATERAL (
              SELECT vv.id, qq.id AS queue_id
              FROM visits vv
              JOIN queue_entries qq ON qq.visit_id = vv.id
              ORDER BY vv.id, qq.id
              LIMIT 1
            ) v
            JOIN queue_entries q ON q.id = v.queue_id
            LIMIT 1;
            """;
        var checkEx = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(tempDb.ConnectionString, invalidGranted));
        Assert.Equal("23514", checkEx.SqlState);
    }

    // =====================================================================================
    // Fixtures / helpers
    // =====================================================================================

    private ClinicalSessionRecoveryService NewService(MedFlowDbContext db, TestClock? clock = null)
        => new(db, new NullRealtimeNotificationPublisher(), clock ?? _clock);

    private async Task<bool> CanConnectAsync()
    {
        try
        {
            return await Db.Database.CanConnectAsync();
        }
        catch
        {
            return false;
        }
    }

    private sealed class Fixture
    {
        public string Suffix { get; init; } = string.Empty;
        public long HospitalId { get; init; } = 1;
        public long OtherHospitalId { get; set; }
        public long OtherDeptId { get; init; }
        public long OtherRoomId { get; init; }
        public long DoctorAUserId { get; init; }
        public long DoctorAId { get; init; }
        public long DoctorBUserId { get; init; }
        public long DoctorBId { get; init; }
        public long DoctorCUserId { get; init; }
        public long DoctorCId { get; init; }
        public long DoctorDUserId { get; init; }
        public long DoctorDId { get; init; }
        public long SeededDoctor2Id { get; init; }
        public long ReceptionUserId { get; init; } = 5;
        public long PatientUserId { get; init; } = 1;
        public long DiagStaffUserId { get; init; } = 7;
        public long PharmacyStaffUserId { get; init; } = 9;
        public long HospitalAdminUserId { get; init; } = 11;
        public long SystemAdminUserId { get; init; } = 13;
        public string PatientEmail { get; init; } = "patient01@medflow.test";
        public string DiagStaffEmail { get; init; } = "diagnostic_staff01@medflow.test";
        public string PharmacyStaffEmail { get; init; } = "pharmacy_staff01@medflow.test";
        public string SystemAdminEmail { get; init; } = "system_admin01@medflow.test";
        public long PatientId { get; init; }
        public long Visit1Id { get; init; }
        public string Visit1Code { get; init; } = string.Empty;
        public long Queue1Id { get; init; }
        public long Visit2Id { get; init; }
        public string Visit2Code { get; init; } = string.Empty;
        public long Queue2Id { get; init; }
        public List<long> UserIds { get; init; } = [];
        public List<long> StaffIds { get; init; } = [];
        public List<long> DoctorIds { get; init; } = [];
        public List<long> RoleIds { get; init; } = [];
        public List<long> ExtraVisitIds { get; } = [];
        public List<long> ExtraQueueIds { get; } = [];
        public List<long> ExtraServicePointIds { get; } = [];
        public List<long> ExtraPolicyIds { get; } = [];
    }

    private async Task<Fixture> CreateFixtureAsync(string tag)
    {
        var suffix = $"{tag}{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var nowLocal = _clock.GetHospitalNow();
        var yesterday = DateOnly.FromDateTime(nowLocal.AddDays(-1));

        long doctorAUserId, doctorAId, doctorBUserId, doctorBId, doctorCUserId, doctorCId, doctorDUserId, doctorDId;
        long visit1Id, queue1Id, visit2Id, queue2Id, otherHospitalId, otherDeptId, otherRoomId;
        long grantRoleId, recoverRoleId;
        var userIds = new List<long>();
        var staffIds = new List<long>();
        var doctorIds = new List<long>();
        var roleIds = new List<long>();

        var grantPerm = await Db.Permissions.AsNoTracking()
            .SingleAsync(p => p.Code == PermissionCodes.ClinicalSessionRecoveryGrant);
        var recoverPerm = await Db.Permissions.AsNoTracking()
            .SingleAsync(p => p.Code == PermissionCodes.ClinicalSessionRecover);

        var otherHospital = new Hospital
        {
            HospitalCode = $"HOS-{suffix}",
            Name = $"Bệnh viện test {suffix}",
            IsActive = true,
        };
        Db.Hospitals.Add(otherHospital);
        await Db.SaveChangesAsync();
        otherHospitalId = otherHospital.Id;

        var otherDept = new Department
        {
            HospitalId = otherHospitalId,
            DepartmentCode = $"D{suffix}"[..Math.Min(40, 1 + suffix.Length)],
            Name = $"Khoa test {suffix}",
            IsActive = true,
        };
        Db.Departments.Add(otherDept);
        await Db.SaveChangesAsync();
        otherDeptId = otherDept.Id;

        var otherRoom = new HospitalRoom
        {
            HospitalId = otherHospitalId,
            DepartmentId = otherDeptId,
            RoomCode = $"R{suffix}"[..Math.Min(40, 1 + suffix.Length)],
            Name = $"Phòng test {suffix}",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
            Capacity = 1,
        };
        Db.HospitalRooms.Add(otherRoom);
        await Db.SaveChangesAsync();
        otherRoomId = otherRoom.Id;

        var grantRole = new Role { Code = $"CSRG{suffix}", Name = "Test GRANT role" };
        var recoverRole = new Role { Code = $"CSRR{suffix}", Name = "Test RECOVER role" };
        Db.Roles.AddRange(grantRole, recoverRole);
        await Db.SaveChangesAsync();
        grantRoleId = grantRole.Id;
        recoverRoleId = recoverRole.Id;
        roleIds.AddRange([grantRoleId, recoverRoleId]);

        Db.RolePermissions.AddRange(
            new RolePermission { RoleId = grantRoleId, PermissionId = grantPerm.Id },
            new RolePermission { RoleId = recoverRoleId, PermissionId = recoverPerm.Id });

        var doctorRoleId = await Db.Roles.AsNoTracking()
            .Where(r => r.Code == RoleNames.Doctor)
            .Select(r => r.Id)
            .SingleAsync();

        async Task<long> CreateDoctorUserAsync(string label, long hospitalId, long departmentId, bool grant, bool recover)
        {
            var user = new User
            {
                Email = $"csr-{tag}-{label}-{suffix}@medflow.test".ToLowerInvariant(),
                AccountStatus = AccountStatus.ACTIVE,
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            userIds.Add(user.Id);

            var staff = new Staff
            {
                UserId = user.Id,
                HospitalId = hospitalId,
                DepartmentId = departmentId,
                StaffCode = $"STF{suffix}{label}",
                FullName = $"Test Doctor {label} {suffix}",
                Status = "ACTIVE",
            };
            Db.Staff.Add(staff);
            await Db.SaveChangesAsync();
            staffIds.Add(staff.Id);

            var doctor = new Doctor
            {
                StaffId = staff.Id,
                HospitalId = hospitalId,
                DoctorCode = $"DOCS{suffix}{label}",
                SpecialtyId = 1,
                IsActive = true,
            };
            Db.Doctors.Add(doctor);
            await Db.SaveChangesAsync();
            doctorIds.Add(doctor.Id);

            Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = doctorRoleId });

            if (grant)
            {
                Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = grantRoleId });
            }

            if (recover)
            {
                Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = recoverRoleId });
            }

            await Db.SaveChangesAsync();
            return user.Id;
        }
        doctorAUserId = await CreateDoctorUserAsync("A", 1, 2, grant: true, recover: true);
        doctorBUserId = await CreateDoctorUserAsync("B", 1, 2, grant: false, recover: true);
        doctorCUserId = await CreateDoctorUserAsync("C", 1, 2, grant: false, recover: true);
        doctorDUserId = await CreateDoctorUserAsync("D", otherHospitalId, otherDeptId, grant: false, recover: true);

        doctorAId = doctorIds[0];
        doctorBId = doctorIds[1];
        doctorCId = doctorIds[2];
        doctorDId = doctorIds[3];
        var seededDoctor2Id = await Db.Doctors.AsNoTracking()
            .Where(d => d.DoctorCode == "DOC-002")
            .Select(d => d.Id)
            .SingleAsync();

        var patient = new Patient
        {
            PatientCode = $"PT{suffix}",
            FullName = $"Test Patient {suffix}",
            Gender = "MALE",
            Status = "ACTIVE",
        };
        Db.Patients.Add(patient);
        await Db.SaveChangesAsync();

        (visit1Id, queue1Id) = await CreateVisitAndQueueAsync(
            $"VIS-{suffix}-1", 1, doctorAId, "RETURN_CONSULTATION", "IN_PROGRESS", 4, yesterday, nowLocal, examinationRoomId: 1);
        await Db.Visits.Where(v => v.Id == visit1Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.PatientId, patient.Id));
        (visit2Id, queue2Id) = await CreateVisitAndQueueAsync(
            $"VIS-{suffix}-2", otherHospitalId, doctorDId, "RETURN_CONSULTATION", "IN_PROGRESS", 2, yesterday, nowLocal, examinationRoomId: otherRoomId);

        return new Fixture
        {
            Suffix = suffix,
            OtherHospitalId = otherHospitalId,
            OtherDeptId = otherDeptId,
            OtherRoomId = otherRoomId,
            DoctorAUserId = doctorAUserId,
            DoctorAId = doctorAId,
            DoctorBUserId = doctorBUserId,
            DoctorBId = doctorBId,
            DoctorCUserId = doctorCUserId,
            DoctorCId = doctorCId,
            DoctorDUserId = doctorDUserId,
            DoctorDId = doctorDId,
            SeededDoctor2Id = seededDoctor2Id,
            PatientId = patient.Id,
            Visit1Id = visit1Id,
            Visit1Code = $"VIS-{suffix}-1",
            Queue1Id = queue1Id,
            Visit2Id = visit2Id,
            Visit2Code = $"VIS-{suffix}-2",
            Queue2Id = queue2Id,
            UserIds = userIds,
            StaffIds = staffIds,
            DoctorIds = doctorIds,
            RoleIds = roleIds,
        };
    }

    private async Task<(long VisitId, long QueueId)> CreateVisitAndQueueAsync(
        string visitCode,
        long hospitalId,
        long assignedDoctorId,
        string queueType,
        string status,
        int stateVersion,
        DateOnly queueDate,
        DateTime nowLocal,
        long? examinationRoomId = null)
    {
        var visit = new Visit
        {
            VisitCode = visitCode,
            PatientId = null,
            AssignedDoctorId = assignedDoctorId,
            // Existing clinical scope is machine-checkable from the visit's examination room department.
            ExaminationRoomId = examinationRoomId,
            Status = status,
            CheckedInAt = nowLocal,
            LastPresentDate = DateOnly.FromDateTime(nowLocal),
            PresenceState = "PRESENT",
            PresenceSessionNo = 1,
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var queue = new QueueEntry
        {
            VisitId = visit.Id,
            QueueDate = queueDate,
            QueueNumber = Random.Shared.Next(1, 9_000_000),
            QueueType = queueType,
            Status = status,
            StateVersion = stateVersion,
            PresenceStatus = "PRESENT",
            ArrivedAt = nowLocal,
            ReadyAt = nowLocal,
        };
        Db.QueueEntries.Add(queue);
        await Db.SaveChangesAsync();

        return (visit.Id, queue.Id);
    }

    private async Task<(long VisitId, string VisitCode, long QueueId)> CreateClinicalSessionAsync(
        Fixture fx,
        string tag,
        long assignedDoctorId,
        string queueType,
        string status,
        int stateVersion,
        long? examinationRoomId = 1)
    {
        var code = $"V{fx.Suffix[..6]}{tag}{Guid.NewGuid():N}"[..20];
        var nowLocal = _clock.GetHospitalNow();
        var (visitId, queueId) = await CreateVisitAndQueueAsync(
            code, 1, assignedDoctorId, queueType, status, stateVersion,
            DateOnly.FromDateTime(nowLocal.AddDays(-1)), nowLocal, examinationRoomId);
        return (visitId, code, queueId);
    }

    private async Task<(long VisitId, string VisitCode, long ServicePointId)> CreateReadyReturnVisitAsync(
        Fixture fx,
        string tag)
    {
        var nowLocal = _clock.GetHospitalNow();
        var sp = new QueueServicePoint
        {
            HospitalId = 1,
            RoomId = 1,
            DoctorId = fx.DoctorAId,
            PointType = "EXAMINATION",
            Name = $"Điểm test {fx.Suffix} {tag}",
            IsActive = true,
        };
        Db.QueueServicePoints.Add(sp);
        await Db.SaveChangesAsync();
        fx.ExtraServicePointIds.Add(sp.Id);

        var code = $"R{fx.Suffix[..5]}{tag}{Guid.NewGuid():N}"[..20];
        var visit = new Visit
        {
            VisitCode = code,
            PatientId = null,
            AssignedDoctorId = fx.DoctorAId,
            ExaminationRoomId = 1,
            Status = "AWAITING_RESULTS",
            CheckedInAt = nowLocal,
            LastPresentDate = DateOnly.FromDateTime(nowLocal),
            PresenceState = "PRESENT",
            ReadyForReturnAt = nowLocal,
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();
        fx.ExtraVisitIds.Add(visit.Id);

        return (visit.Id, code, sp.Id);
    }

    private async Task EnableHospitalAdminReturnCoordinationAsync(Fixture fx, bool enabled)
    {
        var policy = new HospitalPolicy
        {
            HospitalId = 1,
            QueueServicePointId = null,
            PolicyType = CrossDayVisitPolicyKeys.VisitPolicyType,
            PolicyKey = CrossDayVisitPolicyKeys.AllowHospitalAdminReturnCoordination,
            PolicyValue = enabled ? "true" : "false",
            PolicyVersion = 1,
            EffectiveFrom = DateTime.UtcNow.AddHours(-1),
        };
        Db.HospitalPolicies.Add(policy);
        await Db.SaveChangesAsync();
        fx.ExtraPolicyIds.Add(policy.Id);
    }

    private async Task CleanupAsync(Fixture fx)
    {
        var visitIds = fx.ExtraVisitIds.Append(fx.Visit1Id).Append(fx.Visit2Id).ToList();
        var queueIds = fx.ExtraQueueIds.Append(fx.Queue1Id).Append(fx.Queue2Id).ToList();

        var requestIds = await Db.ClinicalSessionRecoveryRequests
            .Where(r => visitIds.Contains(r.VisitId) || queueIds.Contains(r.QueueEntryId))
            .Select(r => r.Id)
            .ToListAsync();
        var requestIdStrings = requestIds.Select(id => id.ToString()).ToList();

        await Db.ClinicalSessionRecoveryRequests
            .Where(r => visitIds.Contains(r.VisitId) || queueIds.Contains(r.QueueEntryId))
            .ExecuteDeleteAsync();

        if (requestIdStrings.Count > 0)
        {
            await Db.AuditLogs.Where(a => a.EntityId != null && requestIdStrings.Contains(a.EntityId))
                .ExecuteDeleteAsync();
        }

        // Remove audit rows authored by the test actors (FK Restrict before users are deleted).
        if (fx.UserIds.Count > 0)
        {
            await Db.AuditLogs
                .Where(a => a.ActorUserId != null && fx.UserIds.Contains(a.ActorUserId.Value))
                .ExecuteDeleteAsync();
        }

        await Db.QueueEntries.Where(q => queueIds.Contains(q.Id) || (q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)))
            .ExecuteDeleteAsync();
        if (fx.ExtraPolicyIds.Count > 0)
        {
            await Db.HospitalPolicies.Where(p => fx.ExtraPolicyIds.Contains(p.Id)).ExecuteDeleteAsync();
        }
        if (fx.ExtraServicePointIds.Count > 0)
        {
            await Db.QueueFairnessStates.Where(f => fx.ExtraServicePointIds.Contains(f.ServicePointId)).ExecuteDeleteAsync();
            await Db.QueueServicePoints.Where(sp => fx.ExtraServicePointIds.Contains(sp.Id)).ExecuteDeleteAsync();
        }
        var medicalRecordIds = await Db.MedicalRecords
            .Where(m => m.VisitId.HasValue && visitIds.Contains(m.VisitId.Value))
            .Select(m => m.Id)
            .ToListAsync();
        if (medicalRecordIds.Count > 0)
        {
            await Db.MedicalRecordRevisions.Where(r => r.MedicalRecordId.HasValue && medicalRecordIds.Contains(r.MedicalRecordId.Value))
                .ExecuteDeleteAsync();
            await Db.MedicalRecords.Where(m => medicalRecordIds.Contains(m.Id))
                .ExecuteDeleteAsync();
        }

        await Db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
        await Db.Patients.Where(p => p.Id == fx.PatientId).ExecuteDeleteAsync();
        await Db.UserRoles.Where(ur => fx.UserIds.Contains(ur.UserId)).ExecuteDeleteAsync();
        await Db.RolePermissions.Where(rp => fx.RoleIds.Contains(rp.RoleId)).ExecuteDeleteAsync();
        await Db.Roles.Where(r => fx.RoleIds.Contains(r.Id)).ExecuteDeleteAsync();
        await Db.Doctors.Where(d => fx.DoctorIds.Contains(d.Id)).ExecuteDeleteAsync();
        await Db.Staff.Where(s => fx.StaffIds.Contains(s.Id)).ExecuteDeleteAsync();
        await Db.Users.Where(u => fx.UserIds.Contains(u.Id)).ExecuteDeleteAsync();
        await Db.HospitalRooms.Where(r => r.Id == fx.OtherRoomId).ExecuteDeleteAsync();
        await Db.Departments.Where(d => d.Id == fx.OtherDeptId).ExecuteDeleteAsync();
        await Db.Hospitals.Where(h => h.Id == fx.OtherHospitalId).ExecuteDeleteAsync();
    }

    private sealed class TestClock : TimeProvider
    {
        private long _utcTicks;

        public TestClock(DateTime vietnamLocalTime) => SetVietnamLocalTime(vietnamLocalTime);

        public void SetVietnamLocalTime(DateTime vietnamLocalTime)
        {
            var unspecified = DateTime.SpecifyKind(vietnamLocalTime, DateTimeKind.Unspecified);
            var dto = new DateTimeOffset(unspecified, TimeSpan.FromHours(7));
            Interlocked.Exchange(ref _utcTicks, dto.UtcDateTime.Ticks);
        }

        public DateTime GetHospitalNow()
            => DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(
                    new DateTime(Interlocked.Read(ref _utcTicks), DateTimeKind.Utc),
                    HospitalClock.TimeZone),
                DateTimeKind.Unspecified);

        public override DateTimeOffset GetUtcNow()
            => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    }

    // =====================================================================================
    // Raw SQL / temp database helpers (Migration 028 test)
    // =====================================================================================

    private sealed record PreflightRow(string Check, long Violations, string Status);

    private static async Task<List<PreflightRow>> RunPreflightAsync(string connectionString, string preflightSql)
    {
        var rows = new List<PreflightRow>();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(preflightSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PreflightRow(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.IsDBNull(3) ? "FAIL" : reader.GetString(3)));
        }

        return rows;
    }

    private static async Task ApplyMigrationsUpTo027Async(string connectionString, string repoRoot)
    {
        var migrationsDir = Path.Combine(repoRoot, "database", "migrations");
        var files = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !name.StartsWith("001_", StringComparison.Ordinal)
                       && string.CompareOrdinal(name, "028_") < 0;
            })
            .ToList();

        foreach (var file in files)
        {
            await ExecuteSqlAsync(connectionString, await File.ReadAllTextAsync(file));
        }
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }

    private static string ResolveAdminConnectionString()
    {
        var baseConn = Environment.GetEnvironmentVariable("MEDFLOW_TEST_DB")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5433;Database=medflow_test;Username=postgres;Password=medflow_dev";
        return new NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = "postgres",
            Pooling = false,
        }.ConnectionString;
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class TempDatabase : IAsyncDisposable
    {
        private readonly string _adminConn;
        public string DatabaseName { get; }
        public string ConnectionString { get; }

        private TempDatabase(string adminConn, string dbName, string connStr)
        {
            _adminConn = adminConn;
            DatabaseName = dbName;
            ConnectionString = connStr;
        }

        public static async Task<TempDatabase> CreateAsync(string adminConn)
        {
            var dbName = $"medflow_csr_{Guid.NewGuid():N}"[..28];
            await using (var admin = new NpgsqlConnection(adminConn))
            {
                await admin.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
                await cmd.ExecuteNonQueryAsync();
            }

            var target = new NpgsqlConnectionStringBuilder(adminConn)
            {
                Database = dbName,
                Pooling = true,
            }.ConnectionString;
            return new TempDatabase(adminConn, dbName, target);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConn);
            await admin.OpenAsync();
            await using (var term = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{DatabaseName}' AND pid <> pg_backend_pid();",
                admin))
            {
                await term.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
