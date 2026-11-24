using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Patients;

public sealed class PatientMedicalRecordEndpointTests : AuthTestBase
{
    private const string ListRoute = "/api/v1/patients/me/medical-records";
    private const string MissingDetailRoute = "/api/v1/patients/me/medical-records/EMR-999999999";

    public PatientMedicalRecordEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task PatientMedicalRecordEndpoints_Unauthenticated_Return401()
    {
        var listResponse = await Client.GetAsync(ListRoute);
        var detailResponse = await Client.GetAsync(MissingDetailRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);
    }

    [Theory]
    [InlineData("DOCTOR")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task PatientMedicalRecordEndpoints_NonPatientRoles_Return403(string role)
    {
        var listResponse = await Client.SendAsync(AuthedGet(ListRoute, userId: 5, role));
        var detailResponse = await Client.SendAsync(AuthedGet(MissingDetailRoute, userId: 5, role));

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);
    }

    [Fact]
    public async Task PatientMedicalRecordEndpoints_MissingUserOrRecord_Return404()
    {
        var missingUserList = await Client.SendAsync(AuthedGet(ListRoute, userId: 817_000_999, "PATIENT"));
        Assert.Equal(HttpStatusCode.NotFound, missingUserList.StatusCode);

        var unknownDetail = await Client.SendAsync(AuthedGet(MissingDetailRoute, userId: 1, "PATIENT"));
        Assert.Equal(HttpStatusCode.NotFound, unknownDetail.StatusCode);
    }

    [Fact]
    public async Task PatientMedicalRecordEndpoints_EndToEnd_EnforcesPhiFamilyLinksAndWritesAuditLogs()
    {
        var seed = await SeedAsync();
        try
        {
            // 1. List returns self + OTP family member, excludes LEGACY + PENDING + stranger
            var listResponse = await Client.SendAsync(AuthedGet(ListRoute, userId: 1, "PATIENT"));
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

            var listPayload = await listResponse.Content.ReadFromJsonAsync<PatientMedicalRecordListResponseDto>();
            Assert.NotNull(listPayload);
            Assert.Contains(listPayload!.Items, item => item.MedicalRecordId == seed.SelfRecordId);
            Assert.Contains(listPayload.Items, item => item.MedicalRecordId == seed.OtpMemberRecordId);
            Assert.DoesNotContain(listPayload.Items, item => item.MedicalRecordId == seed.LegacyMemberRecordId);
            Assert.DoesNotContain(listPayload.Items, item => item.MedicalRecordId == seed.PendingMemberRecordId);
            Assert.DoesNotContain(listPayload.Items, item => item.MedicalRecordId == seed.StrangerRecordId);

            // 2. Detail 200 OK for self by EMR-{id} and by visitCode
            var selfDetailResp = await Client.SendAsync(
                AuthedGet($"{ListRoute}/EMR-{seed.SelfRecordId}", userId: 1, "PATIENT"));
            Assert.Equal(HttpStatusCode.OK, selfDetailResp.StatusCode);
            var selfDetail = await selfDetailResp.Content.ReadFromJsonAsync<PatientMedicalRecordDetailDto>();
            Assert.NotNull(selfDetail);
            Assert.Equal(seed.SelfVisitCode, selfDetail!.VisitCode);
            Assert.Equal("Viêm họng cấp ổn định, uống thuốc đủ liều.", selfDetail.ClinicalConclusion);
            Assert.Equal("118/76 mmHg", selfDetail.VitalSigns?.BloodPressureLabel);
            Assert.Contains(selfDetail.Diagnoses, d => d.Icd10Code == "J02.9" && d.IsPrimary);

            var byVisitCodeResp = await Client.SendAsync(
                AuthedGet($"{ListRoute}/{seed.SelfVisitCode}", userId: 1, "PATIENT"));
            Assert.Equal(HttpStatusCode.OK, byVisitCodeResp.StatusCode);

            // 3. Detail 200 OK for OTP-verified family member
            var otpDetailResp = await Client.SendAsync(
                AuthedGet($"{ListRoute}/{seed.OtpMemberRecordId}", userId: 1, "PATIENT"));
            Assert.Equal(HttpStatusCode.OK, otpDetailResp.StatusCode);

            // 4. LEGACY / PENDING / Stranger blocked with 403 Forbidden
            var legacyFilterResp = await Client.SendAsync(
                AuthedGet($"{ListRoute}?patientId={seed.LegacyPatientId}", userId: 1, "PATIENT"));
            Assert.Equal(HttpStatusCode.Forbidden, legacyFilterResp.StatusCode);

            var legacyDetailResp = await Client.SendAsync(
                AuthedGet($"{ListRoute}/{seed.LegacyMemberRecordId}", userId: 1, "PATIENT"));
            Assert.Equal(HttpStatusCode.Forbidden, legacyDetailResp.StatusCode);

            var pendingDetailResp = await Client.SendAsync(
                AuthedGet($"{ListRoute}/{seed.PendingMemberRecordId}", userId: 1, "PATIENT"));
            Assert.Equal(HttpStatusCode.Forbidden, pendingDetailResp.StatusCode);

            var strangerDetailResp = await Client.SendAsync(
                AuthedGet($"{ListRoute}/{seed.StrangerRecordId}", userId: 1, "PATIENT"));
            Assert.Equal(HttpStatusCode.Forbidden, strangerDetailResp.StatusCode);

            // 5. Audit logs persisted without leaking clinical conclusion
            Db.ChangeTracker.Clear();
            var recordEntityId = seed.SelfRecordId.ToString();
            var detailAudits = await Db.AuditLogs.AsNoTracking()
                .Where(a =>
                    a.ActorUserId == 1 &&
                    a.Action == PatientMedicalRecordService.AuditActionRecordViewed &&
                    a.EntityType == "MEDICAL_RECORD" &&
                    a.EntityId == recordEntityId)
                .ToListAsync();

            Assert.NotEmpty(detailAudits);
            Assert.All(detailAudits, audit =>
                Assert.DoesNotContain("Viêm họng cấp", audit.ChangeData ?? string.Empty));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    private HttpRequestMessage AuthedGet(string url, long userId, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(
            new User { Id = userId, Email = $"{role.ToLowerInvariant()}@medflow.test" },
            [role]).AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<EmrSeed> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var ownerPatient = await Db.Patients.AsNoTracking().FirstAsync(p => p.UserId == 1);
        var staff = await Db.Staff.AsNoTracking().FirstAsync(s => s.UserId == 5);

        var room = new HospitalRoom
        {
            HospitalId = staff.HospitalId,
            RoomCode = $"ER{suffix}"[..8],
            Name = $"Phòng EMR {suffix}",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);

        var otpMember = new Patient { PatientCode = $"EO-{suffix}", FullName = "Người thân OTP", Status = "ACTIVE" };
        var legacyMember = new Patient { PatientCode = $"EL-{suffix}", FullName = "Người thân LEGACY", Status = "ACTIVE" };
        var pendingMember = new Patient { PatientCode = $"EP-{suffix}", FullName = "Người thân PENDING", Status = "ACTIVE" };
        var stranger = new Patient { PatientCode = $"ES-{suffix}", FullName = "Người lạ EMR", Status = "ACTIVE" };
        Db.Patients.AddRange(otpMember, legacyMember, pendingMember, stranger);
        await Db.SaveChangesAsync();

        Db.PatientFamilyLinks.AddRange(
            new PatientFamilyLink
            {
                OwnerPatientId = ownerPatient.Id,
                MemberPatientId = otpMember.Id,
                Relationship = "Con gái",
                VerificationStatus = "VERIFIED",
                VerificationMethod = "OTP",
            },
            new PatientFamilyLink
            {
                OwnerPatientId = ownerPatient.Id,
                MemberPatientId = legacyMember.Id,
                Relationship = "Bố ruột",
                VerificationStatus = "VERIFIED",
                VerificationMethod = "LEGACY",
                LegacyAccessUntil = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(20), DateTimeKind.Unspecified),
            },
            new PatientFamilyLink
            {
                OwnerPatientId = ownerPatient.Id,
                MemberPatientId = pendingMember.Id,
                Relationship = "Vợ",
                VerificationStatus = "PENDING",
                VerificationMethod = null,
            });
        await Db.SaveChangesAsync();

        var completedAt = new DateTime(2026, 11, 18, 10, 0, 0);
        var selfVisitCode = $"EMRV-S-{suffix}";

        var (selfVisitId, selfRecordId) = await CreateCompletedEmrBundleAsync(
            ownerPatient.Id,
            selfVisitCode,
            room.Id,
            staff.Id,
            completedAt);
        var (otpVisitId, otpRecordId) = await CreateCompletedEmrBundleAsync(
            otpMember.Id,
            $"EMRV-O-{suffix}",
            room.Id,
            staff.Id,
            completedAt.AddMinutes(-10));
        var (legacyVisitId, legacyRecordId) = await CreateCompletedEmrBundleAsync(
            legacyMember.Id,
            $"EMRV-L-{suffix}",
            room.Id,
            staff.Id,
            completedAt.AddMinutes(-20));
        var (pendingVisitId, pendingRecordId) = await CreateCompletedEmrBundleAsync(
            pendingMember.Id,
            $"EMRV-P-{suffix}",
            room.Id,
            staff.Id,
            completedAt.AddMinutes(-30));
        var (strangerVisitId, strangerRecordId) = await CreateCompletedEmrBundleAsync(
            stranger.Id,
            $"EMRV-X-{suffix}",
            room.Id,
            staff.Id,
            completedAt.AddMinutes(-40));

        return new EmrSeed(
            OwnerPatientId: ownerPatient.Id,
            SelfVisitCode: selfVisitCode,
            SelfRecordId: selfRecordId,
            OtpMemberRecordId: otpRecordId,
            LegacyPatientId: legacyMember.Id,
            LegacyMemberRecordId: legacyRecordId,
            PendingMemberRecordId: pendingRecordId,
            StrangerRecordId: strangerRecordId,
            RoomId: room.Id,
            CreatedPatientIds: [otpMember.Id, legacyMember.Id, pendingMember.Id, stranger.Id],
            CreatedVisitIds: [selfVisitId, otpVisitId, legacyVisitId, pendingVisitId, strangerVisitId],
            CreatedRecordIds: [selfRecordId, otpRecordId, legacyRecordId, pendingRecordId, strangerRecordId]);
    }

    private async Task<(long VisitId, long RecordId)> CreateCompletedEmrBundleAsync(
        long patientId,
        string visitCode,
        long roomId,
        long checkedInStaffId,
        DateTime completedAt)
    {
        var visit = new Visit
        {
            VisitCode = visitCode,
            PatientId = patientId,
            ExaminationRoomId = roomId,
            CheckedInByStaffId = checkedInStaffId,
            ReasonForVisit = "Đau họng, sốt nhẹ",
            Status = "COMPLETED",
            CheckedInAt = completedAt.AddMinutes(-45),
            CompletedAt = completedAt,
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var record = new MedicalRecord
        {
            VisitId = visit.Id,
            Status = "FINALIZED",
            FinalizedAt = completedAt,
        };
        Db.MedicalRecords.Add(record);
        await Db.SaveChangesAsync();

        Db.MedicalRecordRevisions.Add(new MedicalRecordRevision
        {
            MedicalRecordId = record.Id,
            RevisionNo = 1,
            PresentingComplaint = "Đau rát họng 2 ngày",
            VitalSigns = """{"bp_systolic_mmhg":118,"bp_diastolic_mmhg":76,"pulse_bpm":78,"temperature_c":37.2}""",
            ClinicalFindings = "Niêm mạc họng đỏ nhẹ",
            ClinicalConclusion = "Viêm họng cấp ổn định, uống thuốc đủ liều.",
            RecordedAt = completedAt,
            IsConfirmed = true,
        });

        Db.VisitDiagnoses.Add(new VisitDiagnosis
        {
            VisitId = visit.Id,
            CodeSnapshot = "J02.9",
            NameSnapshot = "Viêm họng cấp, không đặc hiệu",
            ClinicalDiagnosis = "Viêm họng cấp",
            IsPrimary = true,
            Status = "CONFIRMED",
            ConfirmedAt = completedAt,
            RecordedAt = completedAt,
        });
        await Db.SaveChangesAsync();

        return (visit.Id, record.Id);
    }

    private async Task CleanupAsync(EmrSeed seed)
    {
        Db.ChangeTracker.Clear();

        var recordIdStrings = seed.CreatedRecordIds.Select(id => id.ToString()).ToList();
        var auditRows = await Db.AuditLogs
            .Where(a =>
                a.Action == PatientMedicalRecordService.AuditActionRecordViewed &&
                (recordIdStrings.Contains(a.EntityId ?? string.Empty) || a.ActorUserId == 1))
            .ToListAsync();
        Db.AuditLogs.RemoveRange(auditRows);

        Db.VisitDiagnoses.RemoveRange(
            await Db.VisitDiagnoses.Where(d => d.VisitId != null && seed.CreatedVisitIds.Contains(d.VisitId.Value)).ToListAsync());
        Db.MedicalRecordRevisions.RemoveRange(
            await Db.MedicalRecordRevisions.Where(r => r.MedicalRecordId != null && seed.CreatedRecordIds.Contains(r.MedicalRecordId.Value)).ToListAsync());
        Db.MedicalRecords.RemoveRange(
            await Db.MedicalRecords.Where(r => seed.CreatedRecordIds.Contains(r.Id)).ToListAsync());
        Db.Visits.RemoveRange(
            await Db.Visits.Where(v => seed.CreatedVisitIds.Contains(v.Id)).ToListAsync());
        Db.PatientFamilyLinks.RemoveRange(
            await Db.PatientFamilyLinks.Where(l => l.OwnerPatientId == seed.OwnerPatientId && seed.CreatedPatientIds.Contains(l.MemberPatientId)).ToListAsync());
        await Db.SaveChangesAsync();

        Db.HospitalRooms.RemoveRange(
            await Db.HospitalRooms.Where(r => r.Id == seed.RoomId).ToListAsync());
        Db.Patients.RemoveRange(
            await Db.Patients.Where(p => seed.CreatedPatientIds.Contains(p.Id)).ToListAsync());
        await Db.SaveChangesAsync();
    }

    private sealed record EmrSeed(
        long OwnerPatientId,
        string SelfVisitCode,
        long SelfRecordId,
        long OtpMemberRecordId,
        long LegacyPatientId,
        long LegacyMemberRecordId,
        long PendingMemberRecordId,
        long StrangerRecordId,
        long RoomId,
        IReadOnlyList<long> CreatedPatientIds,
        IReadOnlyList<long> CreatedVisitIds,
        IReadOnlyList<long> CreatedRecordIds);
}
