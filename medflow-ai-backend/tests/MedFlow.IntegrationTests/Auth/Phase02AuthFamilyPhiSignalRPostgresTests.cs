using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>
/// Real-PostgreSQL (<c>medflow_test_p02</c>) regression &amp; concurrency suite for Phase 02 (#840):
/// BUG-P2-01..BUG-P2-08 (Family anti-enumeration, GUARDIAN_CREATED scope &amp; downgrade,
/// durable single-use OTP &amp; claim locking, AuthSession RootSessionId lineage &amp; SignalR eviction,
/// strict PHI visit ownership &amp; verification gate, and staff PATIENT_IDENTITY_VERIFY RBAC).
/// </summary>
public sealed class Phase02AuthFamilyPhiSignalRPostgresTests(TestWebAppFactory factory) : AuthTestBase(factory)
{
    private const string HubPath = "/hubs/clinical";
    private const string IdentityHmacKey = "IntegrationTest_IdentityHash_Key_2026";
    private const string OtpHmacSecret = "IntegrationTest_OtpHmacSecret_2026";

    private static string HashRefreshToken(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private static string ComputeOtpHmacHex(string otp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(OtpHmacSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(otp))).ToLowerInvariant();
    }

    private static byte[] ComputeRecipientFingerprint(string recipient)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(OtpHmacSecret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(recipient.ToLowerInvariant()));
    }

    private static string ComputeCccdSha256(string cccd)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cccd))).ToLowerInvariant();

    private static string ComputeCccdHmac(string cccd)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(IdentityHmacKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(cccd))).ToLowerInvariant();
    }

    private static DateTime VietnamNow()
        => HospitalClock.GetNow(TimeProvider.System);

    private async Task CleanupPhase02DiagnosticArtifactsAsync()
    {
        Db.ChangeTracker.Clear();
        var orderIds = await Db.DiagnosticOrders
            .AsNoTracking()
            .Where(o => o.OrderCode != null && (
                o.OrderCode.StartsWith("CLS-MIN-") ||
                o.OrderCode.StartsWith("CLS-CNF-") ||
                o.OrderCode.StartsWith("CLS-CAN-") ||
                o.OrderCode.StartsWith("CLS-GAT-")))
            .Select(o => (long?)o.Id)
            .ToListAsync();

        if (orderIds.Count > 0)
        {
            var resultIds = await Db.DiagnosticResults
                .AsNoTracking()
                .Where(r => orderIds.Contains(r.DiagnosticOrderId))
                .Select(r => (long?)r.Id)
                .ToListAsync();

            if (resultIds.Count > 0)
            {
                await Db.DiagnosticResultVersions
                    .Where(v => resultIds.Contains(v.DiagnosticResultId))
                    .ExecuteDeleteAsync();
                await Db.DiagnosticResults
                    .Where(r => orderIds.Contains(r.DiagnosticOrderId))
                    .ExecuteDeleteAsync();
            }

            await Db.DiagnosticOrders
                .Where(o => orderIds.Contains(o.Id))
                .ExecuteDeleteAsync();
        }

        await Db.Prescriptions
            .Where(rx => rx.PrescriptionCode != null && rx.PrescriptionCode.StartsWith("RX-CNF-"))
            .ExecuteDeleteAsync();

        Db.ChangeTracker.Clear();
    }

    private HubConnection CreateHubConnection(string accessToken)
    {
        var hubUri = new Uri(Factory.Server.BaseAddress, HubPath);
        return new HubConnectionBuilder()
            .WithUrl(
                hubUri,
                options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    options.Headers["Origin"] = "http://localhost:5173";
                    options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                    options.Transports = HttpTransportType.WebSockets;
                    options.WebSocketFactory = async (context, ct) =>
                    {
                        var wsClient = Factory.Server.CreateWebSocketClient();
                        wsClient.ConfigureRequest = req => req.Headers.Origin = "http://localhost:5173";
                        var targetUri = context.Uri;
                        if (!targetUri.Query.Contains("access_token=", StringComparison.OrdinalIgnoreCase))
                        {
                            var sep = string.IsNullOrEmpty(targetUri.Query) ? "?" : "&";
                            targetUri = new Uri($"{targetUri}{sep}access_token={Uri.EscapeDataString(accessToken)}");
                        }

                        return await wsClient.ConnectAsync(targetUri, ct);
                    };
                })
            .Build();
    }

    private async Task<(User User, Patient Patient, LoginResponse Tokens)> CreateVerifiedPatientSessionAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@medflow.test";
        using var scope = Factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var otpService = scope.ServiceProvider.GetRequiredService<AuthOtpService>();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        await authService.RegisterAsync(ValidRegister(email), "127.0.0.1");
        var otp = Factory.EmailSender.LatestOtpFor(email)
            ?? throw new InvalidOperationException("Missing OTP for test registration.");
        await otpService.VerifyOtpAsync(new VerifyOtpRequest { Email = email, Otp = otp });
        var tokens = await authService.LoginAsync(new LoginRequest { Email = email, Password = "MedFlow@2026" }, "127.0.0.1");

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.UserId == user.Id);
        return (user, patient, tokens);
    }

    private async Task<(long UserId, long StaffId, string Token)> CreateStaffTokenAsync(
        string roleCode,
        long hospitalId,
        string suffix)
    {
        var role = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == roleCode);
        var user = new User
        {
            Email = $"staff-{roleCode.ToLowerInvariant()}-{suffix}@medflow.test",
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        var staff = new Staff
        {
            UserId = user.Id,
            HospitalId = hospitalId,
            StaffCode = $"P2{roleCode[0]}{Guid.NewGuid():N}"[..11].ToUpperInvariant(),
            FullName = $"Staff {roleCode} {suffix}",
            Status = "ACTIVE",
        };
        Db.Staff.Add(staff);
        await Db.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();
        var sessionSvc = scope.ServiceProvider.GetRequiredService<IAuthSessionService>();
        var jwtSvc = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var session = await sessionSvc.GenerateAsync(user.Id, "127.0.0.1");
        var token = jwtSvc.GenerateAccessToken(user, [roleCode], session.Entity.Id).AccessToken;
        return (user.Id, staff.Id, token);
    }

    private async Task<long> CreateIsolatedDepartmentAsync(long hospitalId, string suffix)
    {
        var dept = new Department
        {
            HospitalId = hospitalId,
            DepartmentCode = $"P2D-{suffix}".ToUpperInvariant(),
            Name = $"Khoa Kiểm Thử P2 {suffix}",
            IsActive = true,
        };
        Db.Departments.Add(dept);
        await Db.SaveChangesAsync();
        return dept.Id;
    }

    // ── BUG-P2-03: Durable OTP Attempts Under Concurrency & Single-Use Anti-Replay ──

    [Fact]
    public async Task OtpConcurrency_10ParallelWrongCodesPersistLock_And10ParallelValidCodesAllowExactlyOne()
    {
        // Part 1: 10 parallel wrong OTP codes against medflow_test_p02 must serialize via TryLockUserForUpdateAsync
        // and persist Attempts == MaxAttempts (5) + Status == LOCKED despite throwing ValidationException.
        var wrongEmail = $"otp-wrong-{Guid.NewGuid():N}@medflow.test";
        using (var setupScope = Factory.Services.CreateScope())
        {
            var auth = setupScope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.RegisterAsync(ValidRegister(wrongEmail), "127.0.0.1");
        }

        var wrongOtp = Factory.EmailSender.LatestOtpFor(wrongEmail) == "000000" ? "999999" : "000000";
        var wrongTasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            using var scope = Factory.Services.CreateScope();
            var otpSvc = scope.ServiceProvider.GetRequiredService<AuthOtpService>();
            return await Assert.ThrowsAsync<ValidationException>(() =>
                otpSvc.VerifyOtpAsync(new VerifyOtpRequest { Email = wrongEmail, Otp = wrongOtp }));
        });

        await Task.WhenAll(wrongTasks);

        Db.ChangeTracker.Clear();
        var lockedUser = await Db.Users.AsNoTracking().FirstAsync(u => u.Email == wrongEmail);
        var lockedChallenge = await Db.AuthVerificationChallenges.AsNoTracking()
            .FirstAsync(c => c.UserId == lockedUser.Id && c.Purpose == ChallengePurpose.REGISTER_EMAIL);
        Assert.Equal(ChallengeStatus.LOCKED, lockedChallenge.Status);
        Assert.Equal(lockedChallenge.MaxAttempts, lockedChallenge.Attempts);
        Assert.Equal(AccountStatus.PENDING, lockedUser.AccountStatus);

        // Part 2: 10 parallel VALID OTP submissions -> single-use atomic consumption (exactly 1 succeeds, 9 throw).
        var replayEmail = $"otp-replay-{Guid.NewGuid():N}@medflow.test";
        using (var setupScope = Factory.Services.CreateScope())
        {
            var auth = setupScope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.RegisterAsync(ValidRegister(replayEmail), "127.0.0.1");
        }

        var validOtp = Factory.EmailSender.LatestOtpFor(replayEmail)!;
        var replayOutcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var scope = Factory.Services.CreateScope();
            var otpSvc = scope.ServiceProvider.GetRequiredService<AuthOtpService>();
            try
            {
                await otpSvc.VerifyOtpAsync(new VerifyOtpRequest { Email = replayEmail, Otp = validOtp });
                return true;
            }
            catch (ValidationException)
            {
                return false;
            }
        }));

        Assert.Equal(1, replayOutcomes.Count(succeeded => succeeded));
        Assert.Equal(9, replayOutcomes.Count(succeeded => !succeeded));

        // Part 3: Identity-claim step-up OTP wrong code also persists incremented Attempts in DB.
        var (claimUser, _, _) = await CreateVerifiedPatientSessionAsync("otp-claim");
        Db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
        {
            UserId = claimUser.Id,
            Purpose = ChallengePurpose.IDENTITY_CLAIM,
            Channel = ChallengeChannel.EMAIL,
            RecipientFingerprint = ComputeRecipientFingerprint(claimUser.Email),
            SecretHash = ComputeOtpHmacHex("654321"),
            Status = ChallengeStatus.PENDING,
            Attempts = 0,
            MaxAttempts = 5,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        });
        await Db.SaveChangesAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var otpSvc = scope.ServiceProvider.GetRequiredService<AuthOtpService>();
            await Assert.ThrowsAsync<ValidationException>(() =>
                otpSvc.VerifyIdentityClaimOtpAsync(claimUser.Id, "111111"));
        }

        Db.ChangeTracker.Clear();
        var claimChallenge = await Db.AuthVerificationChallenges.AsNoTracking()
            .FirstAsync(c => c.UserId == claimUser.Id && c.Purpose == ChallengePurpose.IDENTITY_CLAIM);
        Assert.Equal(1, claimChallenge.Attempts);
        Assert.Equal(ChallengeStatus.PENDING, claimChallenge.Status);
    }

    // ── BUG-P2-01: Family Link CCCD Anti-Enumeration & Strict Clinical/ValidUntil Gating ──

    [Fact]
    public async Task FamilyLink_CccdAntiEnumeration_UniformRedactedResponse_AndValidUntilPhiGating()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var victimCccd = $"079{Random.Shared.Next(100_000_000, 999_999_999)}";
        var absentCccd = $"080{Random.Shared.Next(100_000_000, 999_999_999)}";

        var victim = new Patient
        {
            PatientCode = $"PAT-VIC-{suffix}".ToUpperInvariant(),
            FullName = "Lê Hoàng Nạn Nhân",
            DateOfBirth = new DateOnly(1984, 7, 20),
            Gender = "MALE",
            Phone = "0909111222",
            Email = $"victim-{suffix}@secret.vn",
            BloodType = "AB-",
            Address = "99 Bí Mật, Quận 1",
            IdentificationHash = ComputeCccdSha256(victimCccd),
            IdentificationHashHmac = ComputeCccdHmac(victimCccd),
            HashVersion = 2,
            Status = "ACTIVE",
        };
        Db.Patients.Add(victim);
        await Db.SaveChangesAsync();

        Db.Visits.Add(new Visit
        {
            VisitCode = $"VIS-VIC-{suffix}".ToUpperInvariant(),
            PatientId = victim.Id,
            Status = "COMPLETED",
            CheckedInAt = VietnamNow().AddDays(-2),
        });
        Db.Appointments.Add(new Appointment
        {
            AppointmentCode = $"APT-VIC-{suffix}".ToUpperInvariant(),
            PatientId = victim.Id,
            BookedAt = VietnamNow(),
            Status = "CONFIRMED",
        });
        await Db.SaveChangesAsync();

        var (_, _, requesterATokens) = await CreateVerifiedPatientSessionAsync("fam-enum-a");
        var (requesterBUser, requesterBPatient, requesterBTokens) = await CreateVerifiedPatientSessionAsync("fam-enum-b");
        var (_, _, requesterCTokens) = await CreateVerifiedPatientSessionAsync("fam-enum-c");

        async Task<(HttpStatusCode Status, FamilyMemberResponse Body)> PostFamilyAsync(string token, CreateFamilyMemberRequest req)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members")
            {
                Content = JsonContent.Create(req),
            };
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await Client.SendAsync(msg);
            var body = await res.Content.ReadFromJsonAsync<FamilyMemberResponse>();
            return (res.StatusCode, body!);
        }

        // (a) Existing CCCD + matching FullName & BirthDate
        var (statusMatch, bodyMatch) = await PostFamilyAsync(requesterATokens.AccessToken, new CreateFamilyMemberRequest
        {
            FullName = "Lê Hoàng Nạn Nhân",
            Relationship = "Bố ruột",
            Gender = "Nam",
            BirthDate = "1984-07-20",
            Address = "10 Lê Lợi, Hà Nội",
            IdentificationNo = victimCccd,
        });

        // (b) Existing CCCD + MISMATCHED FullName & BirthDate (MUST NOT return 409!)
        var (statusMismatch, bodyMismatch) = await PostFamilyAsync(requesterBTokens.AccessToken, new CreateFamilyMemberRequest
        {
            FullName = "Tên Sai Hoàn Toàn",
            Relationship = "Bố ruột",
            Gender = "Nam",
            BirthDate = "1975-01-01",
            Address = "10 Lê Lợi, Hà Nội",
            IdentificationNo = victimCccd,
        });

        // (c) Non-existent CCCD
        var (statusAbsent, bodyAbsent) = await PostFamilyAsync(requesterCTokens.AccessToken, new CreateFamilyMemberRequest
        {
            FullName = "Người Chưa Có Hồ Sơ",
            Relationship = "Bố ruột",
            Gender = "Nam",
            BirthDate = "1978-03-03",
            Address = "10 Lê Lợi, Hà Nội",
            IdentificationNo = absentCccd,
        });

        Assert.Equal(HttpStatusCode.Created, statusMatch);
        Assert.Equal(HttpStatusCode.Created, statusMismatch);
        Assert.Equal(HttpStatusCode.Created, statusAbsent);

        foreach (var redacted in new[] { bodyMatch, bodyMismatch, bodyAbsent })
        {
            Assert.Null(redacted.PatientCode);
            Assert.Null(redacted.Phone);
            Assert.Null(redacted.Email);
            Assert.Null(redacted.Vitals?.BloodType);
            Assert.Equal(0, redacted.OutpatientVisitsCount);
            Assert.False(redacted.HasActiveAppointment);
            Assert.Null(redacted.ActiveAppointmentDetails);
            Assert.False(redacted.Permissions!.CanBookAppointments);
            Assert.False(redacted.Permissions.CanViewMedicalRecords);
            Assert.Equal("PENDING", redacted.Permissions.RelationshipStatus);
        }

        // Internal audit records demographic mismatch for staff review
        Db.ChangeTracker.Clear();
        var reviewAudit = await Db.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ActorUserId == requesterBUser.Id && a.Action == "FAMILY_LINK_PENDING_REVIEW");
        Assert.NotNull(reviewAudit);
        using (var auditDoc = JsonDocument.Parse(reviewAudit.ChangeData!))
        {
            Assert.False(auditDoc.RootElement.GetProperty("fullNameMatched").GetBoolean());
        }

        // Expired ValidUntil hides member from GetFamilyMembersAsync & GetFamilyMemberDetailAsync
        var linkB = await Db.PatientFamilyLinks
            .FirstAsync(l => l.OwnerPatientId == requesterBPatient.Id && l.MemberPatientId == victim.Id);
        linkB.VerificationStatus = "VERIFIED";
        linkB.VerificationMethod = "STAFF";
        linkB.ValidUntil = VietnamNow().AddMinutes(-5);
        await Db.SaveChangesAsync();

        using var detailReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/patients/me/family-members/{victim.Id}");
        detailReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", requesterBTokens.AccessToken);
        var detailRes = await Client.SendAsync(detailReq);
        Assert.Equal(HttpStatusCode.NotFound, detailRes.StatusCode);
    }

    // ── BUG-P2-02, BUG-P2-03 & BUG-P2-06: GUARDIAN_CREATED Scope, Claim Downgrade + SignalR Eviction, & Staff RBAC ──

    [Fact]
    public async Task GuardianCreated_BlocksPhi_DowngradesAndEvictsSignalROnClaimApproval_AndEnforcesStaffVerifyRbac()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hospitalId = (await Db.Hospitals.AsNoTracking().OrderBy(h => h.Id).FirstAsync()).Id;
        var deptId = await CreateIsolatedDepartmentAsync(hospitalId, $"gp{suffix}");

        var room = new HospitalRoom
        {
            HospitalId = hospitalId,
            DepartmentId = deptId,
            RoomCode = $"GP{suffix}".ToUpperInvariant(),
            Name = $"Phong Nhi {suffix}",
            RoomType = "EXAMINATION",
            Capacity = 4,
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);
        await Db.SaveChangesAsync();

        var (_, guardianPatient, guardianTokens) = await CreateVerifiedPatientSessionAsync("guardian");

        // Attempting to link an existing adult with CCCD NEVER gets GUARDIAN_CREATED (always PENDING)
        var existingAdultCccd = $"082{Random.Shared.Next(100_000_000, 999_999_999)}";
        var existingAdult = new Patient
        {
            PatientCode = $"PAT-ADL-{suffix}".ToUpperInvariant(),
            FullName = "Người Lớn Đã Có Tài Khoản",
            DateOfBirth = new DateOnly(1990, 4, 10),
            Gender = "FEMALE",
            IdentificationHash = ComputeCccdSha256(existingAdultCccd),
            IdentificationHashHmac = ComputeCccdHmac(existingAdultCccd),
            HashVersion = 2,
            Status = "ACTIVE",
        };
        Db.Patients.Add(existingAdult);
        await Db.SaveChangesAsync();

        using (var adultCccdReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members")
        {
            Content = JsonContent.Create(new CreateFamilyMemberRequest
            {
                FullName = "Người Lớn Đã Có Tài Khoản",
                Relationship = "Vợ",
                Gender = "Nữ",
                BirthDate = "1990-04-10",
                Address = "15 Hai Bà Trưng, Hà Nội",
                IdentificationNo = existingAdultCccd,
            }),
        })
        {
            adultCccdReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", guardianTokens.AccessToken);
            var adultCccdRes = await Client.SendAsync(adultCccdReq);
            Assert.Equal(HttpStatusCode.Created, adultCccdRes.StatusCode);
            var adultBody = await adultCccdRes.Content.ReadFromJsonAsync<FamilyMemberResponse>();
            Assert.Equal("PENDING", adultBody!.Permissions!.RelationshipStatus);
            Assert.False(adultBody.Permissions.CanBookAppointments);
        }

        // Create minor (< 18) without CCCD -> VERIFIED + GUARDIAN_CREATED
        FamilyMemberResponse minorCreated;
        using (var minorReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members")
        {
            Content = JsonContent.Create(new CreateFamilyMemberRequest
            {
                FullName = "Nguyễn Bé Con",
                Relationship = "Con trai",
                Gender = "Nam",
                BirthDate = "2018-09-01",
                Address = "15 Hai Bà Trưng, Hà Nội",
            }),
        })
        {
            minorReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", guardianTokens.AccessToken);
            var minorRes = await Client.SendAsync(minorReq);
            Assert.Equal(HttpStatusCode.Created, minorRes.StatusCode);
            minorCreated = (await minorRes.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        }

        Assert.True(minorCreated.Permissions!.CanBookAppointments);
        Assert.False(minorCreated.Permissions.CanViewMedicalRecords);

        Db.ChangeTracker.Clear();
        var minorLink = await Db.PatientFamilyLinks.AsNoTracking()
            .FirstAsync(l => l.OwnerPatientId == guardianPatient.Id && l.MemberPatientId == minorCreated.MemberPatientId);
        Assert.Equal("VERIFIED", minorLink.VerificationStatus);
        Assert.Equal("GUARDIAN_CREATED", minorLink.VerificationMethod);

        // Seed a visit + finalized diagnostic result on the minor dependent
        var minorVisit = new Visit
        {
            VisitCode = $"VIS-MIN-{suffix}".ToUpperInvariant(),
            PatientId = minorCreated.MemberPatientId,
            ExaminationRoomId = room.Id,
            Status = "COMPLETED",
            CheckedInAt = VietnamNow().AddHours(-2),
        };
        Db.Visits.Add(minorVisit);
        await Db.SaveChangesAsync();

        var diagService = new Service
        {
            HospitalId = hospitalId,
            DepartmentId = deptId,
            ServiceCode = $"SV-M-{suffix}".ToUpperInvariant(),
            Name = "Xét nghiệm tổng phân tích máu",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        Db.Services.Add(diagService);
        await Db.SaveChangesAsync();

        var minorOrder = new DiagnosticOrder
        {
            OrderCode = $"CLS-MIN-{suffix}".ToUpperInvariant(),
            VisitId = minorVisit.Id,
            ServiceId = diagService.Id,
            Status = "COMPLETED",
            OrderedAt = VietnamNow().AddHours(-1),
        };
        Db.DiagnosticOrders.Add(minorOrder);
        await Db.SaveChangesAsync();

        var minorResult = new DiagnosticResult
        {
            DiagnosticOrderId = minorOrder.Id,
            Status = "FINALIZED",
            FinalizedAt = VietnamNow(),
        };
        Db.DiagnosticResults.Add(minorResult);
        await Db.SaveChangesAsync();

        Db.DiagnosticResultVersions.Add(new DiagnosticResultVersion
        {
            DiagnosticResultId = minorResult.Id,
            VersionNo = 1,
            ResultText = "WBC 7.2",
            Conclusion = "Bình thường",
            IsFinalized = true,
            EnteredAt = VietnamNow(),
        });
        await Db.SaveChangesAsync();

        // GUARDIAN_CREATED CANNOT read PHI diagnostic result (403 Forbidden)
        using (var phiReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/patients/me/diagnostic-results/{minorOrder.OrderCode}"))
        {
            phiReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", guardianTokens.AccessToken);
            var phiRes = await Client.SendAsync(phiReq);
            Assert.Equal(HttpStatusCode.Forbidden, phiRes.StatusCode);
        }

        // Guardian CANNOT join VisitPaymentGroup under GUARDIAN_CREATED, but CAN join AppointmentQueueGroup while minor.UserId == null!
        var minorSlot = new ExaminationSlot
        {
            RoomId = room.Id,
            ServiceId = diagService.Id,
            StartsAt = VietnamNow().AddHours(1),
            EndsAt = VietnamNow().AddHours(2),
            Capacity = 5,
            Status = "OPEN",
        };
        Db.ExaminationSlots.Add(minorSlot);
        await Db.SaveChangesAsync();

        var minorAppointment = new Appointment
        {
            AppointmentCode = $"APT-MIN-{suffix}".ToUpperInvariant(),
            PatientId = minorCreated.MemberPatientId,
            SlotId = minorSlot.Id,
            BookedAt = VietnamNow(),
            Status = "CONFIRMED",
        };
        Db.Appointments.Add(minorAppointment);
        await Db.SaveChangesAsync();

        var revokedEvents = Channel.CreateUnbounded<JsonElement>();
        await using var guardianHub = CreateHubConnection(guardianTokens.AccessToken);
        guardianHub.On<JsonElement>("MembershipRevoked", p => revokedEvents.Writer.TryWrite(p.Clone()));
        await guardianHub.StartAsync();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            guardianHub.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", minorVisit.VisitCode));

        var joinAck = await guardianHub.InvokeAsync<RealtimeSubscriptionAckDto>(
            "JoinAppointmentQueueGroup",
            minorAppointment.AppointmentCode);
        Assert.Equal($"appointment-queue:{minorAppointment.Id}", joinAck.ResourceKey);

        // Now grown child registers own account and claims minorCreated.PatientCode!
        var (childUser, _, childTokens) = await CreateVerifiedPatientSessionAsync("grown-child");
        string claimOtp;
        using (var otpReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/identity-claims/otp"))
        {
            otpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", childTokens.AccessToken);
            var otpRes = await Client.SendAsync(otpReq);
            otpRes.EnsureSuccessStatusCode();
            claimOtp = Factory.EmailSender.LatestOtpFor(childUser.Email)!;
        }

        // BUG-P2-03: Pre-validation rejects empty/invalid patientCode BEFORE burning OTP!
        using (var invalidClaimReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/identity-claims")
        {
            Content = JsonContent.Create(new CreateIdentityClaimRequest("", hospitalId, claimOtp)),
        })
        {
            invalidClaimReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", childTokens.AccessToken);
            var invalidClaimRes = await Client.SendAsync(invalidClaimReq);
            Assert.Equal(HttpStatusCode.BadRequest, invalidClaimRes.StatusCode);
        }

        // Same OTP still works because pre-validation ran BEFORE consuming OTP!
        IdentityClaimResponse createdClaim;
        using (var validClaimReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/identity-claims")
        {
            Content = JsonContent.Create(new CreateIdentityClaimRequest(minorCreated.PatientCode!, hospitalId, claimOtp)),
        })
        {
            validClaimReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", childTokens.AccessToken);
            var validClaimRes = await Client.SendAsync(validClaimReq);
            Assert.Equal(HttpStatusCode.Created, validClaimRes.StatusCode);
            createdClaim = (await validClaimRes.Content.ReadFromJsonAsync<IdentityClaimResponse>())!;
        }

        // BUG-P2-06: staff WITHOUT PATIENT_IDENTITY_VERIFY gets 403 on queue & approve & family-links verify.
        // Migration 033 grants the permission to seeded RECEPTION by owner decision (eKYC slice 6),
        // so the negative actor here is DOCTOR — still lacks the permission.
        var (_, _, doctorToken) = await CreateStaffTokenAsync("DOCTOR", hospitalId, suffix);
        var (_, _, adminToken) = await CreateStaffTokenAsync("HOSPITAL_ADMIN", hospitalId, suffix);

        using (var recQueueReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reception/patient-identity-claims"))
        {
            recQueueReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", doctorToken);
            var recQueueRes = await Client.SendAsync(recQueueReq);
            Assert.Equal(HttpStatusCode.Forbidden, recQueueRes.StatusCode);
        }

        // Hospital Admin (with PATIENT_IDENTITY_VERIFY + matching HospitalId) approves the claim
        using (var approveReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/reception/patient-identity-claims/{createdClaim.ClaimId}/approve"))
        {
            approveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var approveRes = await Client.SendAsync(approveReq);
            Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);
        }

        // Verify GUARDIAN_CREATED link was automatically downgraded to PENDING (VerificationMethod = null)
        Db.ChangeTracker.Clear();
        var downgradedLink = await Db.PatientFamilyLinks.AsNoTracking()
            .FirstAsync(l => l.OwnerPatientId == guardianPatient.Id && l.MemberPatientId == minorCreated.MemberPatientId);
        Assert.Equal("PENDING", downgradedLink.VerificationStatus);
        Assert.Null(downgradedLink.VerificationMethod);

        // Verify guardian's live SignalR subscription received MembershipRevoked!
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var revokedPayload = await revokedEvents.Reader.ReadAsync(cts.Token);
            Assert.Equal($"appointment-queue:{minorAppointment.Id}", revokedPayload.GetProperty("resourceKey").GetString());
        }

        // Staff without PATIENT_IDENTITY_VERIFY gets 403 on family-links/{linkId}/verify
        using (var recVerifyLinkReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/reception/patient-identity-claims/family-links/{guardianPatient.Id}:{minorCreated.MemberPatientId}/verify")
        {
            Content = JsonContent.Create(new VerifyFamilyLinkRequest()),
        })
        {
            recVerifyLinkReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", doctorToken);
            var recVerifyLinkRes = await Client.SendAsync(recVerifyLinkReq);
            Assert.Equal(HttpStatusCode.Forbidden, recVerifyLinkRes.StatusCode);
        }

        // Hospital Admin verifies the downgraded family link -> VERIFIED / STAFF -> unlocks PHI!
        using (var admVerifyLinkReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/reception/patient-identity-claims/family-links/{guardianPatient.Id}:{minorCreated.MemberPatientId}/verify")
        {
            Content = JsonContent.Create(new VerifyFamilyLinkRequest()),
        })
        {
            admVerifyLinkReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var admVerifyLinkRes = await Client.SendAsync(admVerifyLinkReq);
            Assert.Equal(HttpStatusCode.OK, admVerifyLinkRes.StatusCode);
            var verifiedDto = await admVerifyLinkRes.Content.ReadFromJsonAsync<VerifyFamilyLinkResponse>();
            Assert.Equal("VERIFIED", verifiedDto!.VerificationStatus);
            Assert.Equal("STAFF", verifiedDto.VerificationMethod);
        }

        using (var phiAfterStaffVerifyReq = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/patients/me/diagnostic-results/{minorOrder.OrderCode}"))
        {
            phiAfterStaffVerifyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", guardianTokens.AccessToken);
            var phiAfterRes = await Client.SendAsync(phiAfterStaffVerifyReq);
            Assert.Equal(HttpStatusCode.OK, phiAfterRes.StatusCode);
        }

        await CleanupPhase02DiagnosticArtifactsAsync();
    }

    // ── BUG-P2-03: Inactive Target Rejection, ExpirePendingAsync, & RejectAsync on NEEDS_RECONCILIATION ──

    [Fact]
    public async Task IdentityClaim_BlocksInactiveTarget_ExpiresStaleClaims_AndAllowsRejectingNeedsReconciliation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hospitalId = (await Db.Hospitals.AsNoTracking().OrderBy(h => h.Id).FirstAsync()).Id;
        var (adminUserId, _, adminToken) = await CreateStaffTokenAsync("HOSPITAL_ADMIN", hospitalId, $"ic{suffix}");

        // 1. Inactive target patient cannot be linked on ApproveAsync
        var archivedTarget = new Patient
        {
            PatientCode = $"PAT-ARC-{suffix}".ToUpperInvariant(),
            FullName = "Hồ Sơ Lưu Trữ",
            Status = "ARCHIVED",
        };
        Db.Patients.Add(archivedTarget);
        await Db.SaveChangesAsync();

        var (user1, _, _) = await CreateVerifiedPatientSessionAsync($"ic-arc-{suffix}");
        Db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
        {
            UserId = user1.Id,
            Purpose = ChallengePurpose.IDENTITY_CLAIM,
            Channel = ChallengeChannel.EMAIL,
            RecipientFingerprint = ComputeRecipientFingerprint(user1.Email),
            SecretHash = ComputeOtpHmacHex("222333"),
            Status = ChallengeStatus.PENDING,
            MaxAttempts = 5,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        });
        await Db.SaveChangesAsync();

        IdentityClaimResponse arcClaim;
        using (var scope = Factory.Services.CreateScope())
        {
            var claimSvc = scope.ServiceProvider.GetRequiredService<IPatientIdentityClaimService>();
            arcClaim = await claimSvc.CreateAsync(user1.Id, new CreateIdentityClaimRequest(archivedTarget.PatientCode!, hospitalId, "222333"));
            await Assert.ThrowsAsync<ConflictException>(() => claimSvc.ApproveAsync(adminUserId, arcClaim.ClaimId));
        }

        // 2. Stale PENDING claim with ExpiresAt <= nowUtc transitions to EXPIRED on GetMineAsync
        await Db.PatientIdentityClaims
            .Where(c => c.Id == arcClaim.ClaimId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));

        using (var scope = Factory.Services.CreateScope())
        {
            var claimSvc = scope.ServiceProvider.GetRequiredService<IPatientIdentityClaimService>();
            var mine = await claimSvc.GetMineAsync(user1.Id);
            Assert.Contains(mine, c => c.ClaimId == arcClaim.ClaimId && c.Status == "EXPIRED");
        }

        // 3. NEEDS_RECONCILIATION claim (when current patient has clinical visit data) can be rejected via RejectAsync
        var activeTarget = new Patient
        {
            PatientCode = $"PAT-REC-{suffix}".ToUpperInvariant(),
            FullName = "Hồ Sơ Đối Soát",
            Status = "ACTIVE",
        };
        Db.Patients.Add(activeTarget);
        var (user2, current2, _) = await CreateVerifiedPatientSessionAsync($"ic-rec-{suffix}");
        Db.Visits.Add(new Visit
        {
            VisitCode = $"VIS-REC-{suffix}".ToUpperInvariant(),
            PatientId = current2.Id,
            Status = "COMPLETED",
            CheckedInAt = VietnamNow().AddDays(-1),
        });
        Db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
        {
            UserId = user2.Id,
            Purpose = ChallengePurpose.IDENTITY_CLAIM,
            Channel = ChallengeChannel.EMAIL,
            RecipientFingerprint = ComputeRecipientFingerprint(user2.Email),
            SecretHash = ComputeOtpHmacHex("444555"),
            Status = ChallengeStatus.PENDING,
            MaxAttempts = 5,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        });
        await Db.SaveChangesAsync();

        IdentityClaimResponse recClaim;
        using (var scope = Factory.Services.CreateScope())
        {
            var claimSvc = scope.ServiceProvider.GetRequiredService<IPatientIdentityClaimService>();
            recClaim = await claimSvc.CreateAsync(user2.Id, new CreateIdentityClaimRequest(activeTarget.PatientCode!, hospitalId, "444555"));
            var approved = await claimSvc.ApproveAsync(adminUserId, recClaim.ClaimId);
            Assert.Equal("NEEDS_RECONCILIATION", approved.Status);
        }

        using (var rejectReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/reception/patient-identity-claims/{recClaim.ClaimId}/reject")
        {
            Content = JsonContent.Create(new RejectIdentityClaimRequest("Đã đối soát và từ chối hợp lệ")),
        })
        {
            rejectReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var rejectRes = await Client.SendAsync(rejectReq);
            Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);
            var rejected = await rejectRes.Content.ReadFromJsonAsync<IdentityClaimResponse>();
            Assert.Equal("REJECTED", rejected!.Status);
        }
    }

    // ── BUG-P2-04: AuthSession RootSessionId Lineage & Server-Side SignalR Eviction ──

    [Fact]
    public async Task AuthSessionLineage_RotatedChangePasswordKeepsActiveTab_ScopedLogoutSparesOtherDevices_AndResetEvictsAllSignalR()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (user, patient, deviceALogin) = await CreateVerifiedPatientSessionAsync($"lineage-{suffix}");
        var (_, _, foreignTokens) = await CreateVerifiedPatientSessionAsync($"foreign-{suffix}");

        // Login Device B and Device C via IAuthService
        LoginResponse deviceBLogin;
        LoginResponse deviceCLogin;
        RefreshResponse rotatedA1;
        RefreshResponse rotatedA2;
        using (var scope = Factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            deviceBLogin = await auth.LoginAsync(new LoginRequest { Email = user.Email, Password = "MedFlow@2026" }, "10.0.0.2");
            deviceCLogin = await auth.LoginAsync(new LoginRequest { Email = user.Email, Password = "MedFlow@2026" }, "10.0.0.3");

            // Device A silently rotates its refresh token twice (rootA -> A1 -> A2)
            rotatedA1 = await auth.RefreshAsync(new RefreshRequest { RefreshToken = deviceALogin.RefreshToken }, "10.0.0.1");
            rotatedA2 = await auth.RefreshAsync(new RefreshRequest { RefreshToken = rotatedA1.RefreshToken }, "10.0.0.1");
        }

        Db.ChangeTracker.Clear();
        var rootASession = await Db.AuthSessions.AsNoTracking()
            .FirstAsync(s => s.RefreshTokenHash == HashRefreshToken(deviceALogin.RefreshToken));
        var leafA2Session = await Db.AuthSessions.AsNoTracking()
            .FirstAsync(s => s.RefreshTokenHash == HashRefreshToken(rotatedA2.RefreshToken));
        Assert.Equal(rootASession.Id, rootASession.RootSessionId);
        Assert.Equal(rootASession.Id, leafA2Session.RootSessionId);
        Assert.Equal(AuthSessionRevokeReasons.Rotated, rootASession.RevokedReason);

        // Seed a visit for patient so all 3 devices can join SignalR VisitPaymentGroup
        var hospitalId = (await Db.Hospitals.AsNoTracking().OrderBy(h => h.Id).FirstAsync()).Id;
        var deptId = await CreateIsolatedDepartmentAsync(hospitalId, $"ln{suffix}");
        var room = new HospitalRoom
        {
            HospitalId = hospitalId,
            DepartmentId = deptId,
            RoomCode = $"LN{suffix}".ToUpperInvariant(),
            Name = $"Phong Lineage {suffix}",
            RoomType = "EXAMINATION",
            Capacity = 4,
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);
        await Db.SaveChangesAsync();

        var visit = new Visit
        {
            VisitCode = $"VIS-LIN-{suffix}".ToUpperInvariant(),
            PatientId = patient.Id,
            ExaminationRoomId = room.Id,
            Status = "CHECKED_IN",
            CheckedInAt = VietnamNow(),
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var deviceARevoked = Channel.CreateUnbounded<JsonElement>();
        var deviceBRevoked = Channel.CreateUnbounded<JsonElement>();
        var deviceCRevoked = Channel.CreateUnbounded<JsonElement>();

        // Notice Device A connects with its INITIAL access token (sid = rootASession.Id) even after refresh rotation!
        await using var connA = CreateHubConnection(deviceALogin.AccessToken);
        await using var connB = CreateHubConnection(deviceBLogin.AccessToken);
        await using var connC = CreateHubConnection(deviceCLogin.AccessToken);

        connA.On<JsonElement>("MembershipRevoked", p => deviceARevoked.Writer.TryWrite(p.Clone()));
        connB.On<JsonElement>("MembershipRevoked", p => deviceBRevoked.Writer.TryWrite(p.Clone()));
        connC.On<JsonElement>("MembershipRevoked", p => deviceCRevoked.Writer.TryWrite(p.Clone()));

        await Task.WhenAll(connA.StartAsync(), connB.StartAsync(), connC.StartAsync());
        await connA.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", visit.VisitCode);
        await connB.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", visit.VisitCode);
        await connC.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", visit.VisitCode);

        // 1. ChangePassword from Device A (JWT sid = rootA, CurrentRefreshToken = rotatedA2)
        // Must NOT self-evict Device A, and MUST evict Device B & Device C with PASSWORD_CHANGED!
        using (var changePwdReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest
            {
                CurrentPassword = "MedFlow@2026",
                NewPassword = "NewSecure@2027",
                CurrentRefreshToken = rotatedA2.RefreshToken,
            }),
        })
        {
            changePwdReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceALogin.AccessToken);
            var changePwdRes = await Client.SendAsync(changePwdReq);
            Assert.Equal(HttpStatusCode.OK, changePwdRes.StatusCode);
            var changeBody = await changePwdRes.Content.ReadFromJsonAsync<ChangePasswordResponse>();
            Assert.True(changeBody!.KeptCurrentSession);
        }

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var bRevoked = await deviceBRevoked.Reader.ReadAsync(cts.Token);
            var cRevoked = await deviceCRevoked.Reader.ReadAsync(cts.Token);
            Assert.Equal(AuthSessionRevokeReasons.PasswordChanged, bRevoked.GetProperty("reasonCode").GetString());
            Assert.Equal(AuthSessionRevokeReasons.PasswordChanged, cRevoked.GetProperty("reasonCode").GetString());
        }

        // Device A received ZERO revocations and can STILL renew its SignalR subscription!
        Assert.False(deviceARevoked.Reader.TryRead(out _));
        var renewedAckA = await connA.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", visit.VisitCode);
        Assert.Equal($"visit-payment:{visit.Id}", renewedAckA.ResourceKey);

        // Device B's session is revoked in DB -> attempting to re-join on connB is rejected server-side!
        await Assert.ThrowsAnyAsync<Exception>(() =>
            connB.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", visit.VisitCode));

        // 2. Open fresh Device D with new password, then Logout ONLY Device D (and test foreign refresh token safety)
        LoginResponse deviceDLogin;
        using (var scope = Factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            deviceDLogin = await auth.LoginAsync(new LoginRequest { Email = user.Email, Password = "NewSecure@2027" }, "10.0.0.4");
        }

        var deviceDRevoked = Channel.CreateUnbounded<JsonElement>();
        await using var connD = CreateHubConnection(deviceDLogin.AccessToken);
        connD.On<JsonElement>("MembershipRevoked", p => deviceDRevoked.Writer.TryWrite(p.Clone()));
        await connD.StartAsync();
        await connD.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", visit.VisitCode);

        // Passing another user's refresh token to Logout MUST NOT revoke the foreign user's session!
        using (var foreignLogoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(new RefreshRequest { RefreshToken = foreignTokens.RefreshToken }),
        })
        {
            foreignLogoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceDLogin.AccessToken);
            var foreignLogoutRes = await Client.SendAsync(foreignLogoutReq);
            Assert.Equal(HttpStatusCode.NoContent, foreignLogoutRes.StatusCode);
        }

        Db.ChangeTracker.Clear();
        var foreignSession = await Db.AuthSessions.AsNoTracking()
            .FirstAsync(s => s.RefreshTokenHash == HashRefreshToken(foreignTokens.RefreshToken));
        Assert.Null(foreignSession.RevokedAt);

        // Valid single-device Logout on Device D evicts Device D ONLY while Device A stays alive!
        using (var logoutDReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(new RefreshRequest { RefreshToken = deviceDLogin.RefreshToken }),
        })
        {
            logoutDReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceDLogin.AccessToken);
            var logoutDRes = await Client.SendAsync(logoutDReq);
            Assert.Equal(HttpStatusCode.NoContent, logoutDRes.StatusCode);
        }

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var dRevoked = await deviceDRevoked.Reader.ReadAsync(cts.Token);
            Assert.Equal(AuthSessionRevokeReasons.Logout, dRevoked.GetProperty("reasonCode").GetString());
        }
        Assert.False(deviceARevoked.Reader.TryRead(out _));

        // 3. ResetPassword revokes ALL remaining sessions (including Device A) with PASSWORD_RESET and evicts connA!
        using (var scope = Factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, "10.0.0.1");
            var resetOtp = Factory.EmailSender.LatestOtpFor(user.Email)!;
            await auth.ResetPasswordAsync(new ResetPasswordRequest
            {
                Email = user.Email,
                OtpCode = resetOtp,
                NewPassword = "ResetPass@2028",
            }, "10.0.0.1");
        }

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var aRevoked = await deviceARevoked.Reader.ReadAsync(cts.Token);
            Assert.Equal(AuthSessionRevokeReasons.PasswordReset, aRevoked.GetProperty("reasonCode").GetString());
        }

        await Assert.ThrowsAnyAsync<Exception>(() =>
            connA.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", visit.VisitCode));
    }

    // ── BUG-P2-05: LockedUntil, Visit/Appointment Ownership Conflict, Verification Gate, & PHI Audit ──

    [Fact]
    public async Task PhiSecurity_BlocksLockedUser_RejectsVisitAppointmentConflict_EnforcesVerificationGate_AndAuditsPhiReads()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hospitalId = (await Db.Hospitals.AsNoTracking().OrderBy(h => h.Id).FirstAsync()).Id;
        var deptId = await CreateIsolatedDepartmentAsync(hospitalId, $"ph{suffix}");
        var (_, staffId, _) = await CreateStaffTokenAsync("DIAGNOSTIC_STAFF", hospitalId, $"dg{suffix}");

        var (callerUser, callerPatient, callerTokens) = await CreateVerifiedPatientSessionAsync($"phi-caller-{suffix}");
        var (_, otherPatient, _) = await CreateVerifiedPatientSessionAsync($"phi-other-{suffix}");

        var verifiedService = new Service
        {
            HospitalId = hospitalId,
            DepartmentId = deptId,
            ServiceCode = $"SV-VER-{suffix}".ToUpperInvariant(),
            Name = "MRI Sọ não có cản quang",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = true,
            IsActive = true,
        };
        Db.Services.Add(verifiedService);
        await Db.SaveChangesAsync();

        // 1. Corrupt/conflicting visit: visit.PatientId = otherPatient.Id while appointment.PatientId = callerPatient.Id
        var callerAppointmentConflict = new Appointment
        {
            AppointmentCode = $"APT-CNF-{suffix}".ToUpperInvariant(),
            PatientId = callerPatient.Id,
            BookedAt = VietnamNow(),
            Status = "CHECKED_IN",
        };
        var callerAppointmentFallback = new Appointment
        {
            AppointmentCode = $"APT-FBK-{suffix}".ToUpperInvariant(),
            PatientId = callerPatient.Id,
            BookedAt = VietnamNow(),
            Status = "CHECKED_IN",
        };
        Db.Appointments.AddRange(callerAppointmentConflict, callerAppointmentFallback);
        await Db.SaveChangesAsync();

        var conflictingVisit = new Visit
        {
            VisitCode = $"VIS-CNF-{suffix}".ToUpperInvariant(),
            PatientId = otherPatient.Id,
            AppointmentId = callerAppointmentConflict.Id,
            Status = "COMPLETED",
            CheckedInAt = VietnamNow().AddHours(-3),
        };
        var cancelledVisit = new Visit
        {
            VisitCode = $"VIS-CAN-{suffix}".ToUpperInvariant(),
            PatientId = callerPatient.Id,
            Status = "CANCELLED",
            CheckedInAt = VietnamNow().AddHours(-2),
        };
        var validVisit = new Visit
        {
            VisitCode = $"VIS-VAL-{suffix}".ToUpperInvariant(),
            PatientId = null, // Valid fallback to appointment.PatientId when visit.PatientId is null!
            AppointmentId = callerAppointmentFallback.Id,
            Status = "COMPLETED",
            CheckedInAt = VietnamNow().AddHours(-1),
        };
        Db.Visits.AddRange(conflictingVisit, cancelledVisit, validVisit);
        await Db.SaveChangesAsync();

        var conflictOrder = new DiagnosticOrder
        {
            OrderCode = $"CLS-CNF-{suffix}".ToUpperInvariant(),
            VisitId = conflictingVisit.Id,
            ServiceId = verifiedService.Id,
            Status = "COMPLETED",
        };
        var cancelledOrder = new DiagnosticOrder
        {
            OrderCode = $"CLS-CAN-{suffix}".ToUpperInvariant(),
            VisitId = cancelledVisit.Id,
            ServiceId = verifiedService.Id,
            Status = "COMPLETED",
        };
        var gatedOrder = new DiagnosticOrder
        {
            OrderCode = $"CLS-GAT-{suffix}".ToUpperInvariant(),
            VisitId = validVisit.Id,
            ServiceId = verifiedService.Id,
            Status = "COMPLETED",
        };
        Db.DiagnosticOrders.AddRange(conflictOrder, cancelledOrder, gatedOrder);
        await Db.SaveChangesAsync();

        var conflictRes = new DiagnosticResult { DiagnosticOrderId = conflictOrder.Id, Status = "VERIFIED", FinalizedAt = VietnamNow() };
        var cancelledRes = new DiagnosticResult { DiagnosticOrderId = cancelledOrder.Id, Status = "VERIFIED", FinalizedAt = VietnamNow() };
        var gatedRes = new DiagnosticResult { DiagnosticOrderId = gatedOrder.Id, Status = "FINALIZED", FinalizedAt = VietnamNow() };
        Db.DiagnosticResults.AddRange(conflictRes, cancelledRes, gatedRes);
        await Db.SaveChangesAsync();

        var gatedVersion = new DiagnosticResultVersion
        {
            DiagnosticResultId = gatedRes.Id,
            VersionNo = 1,
            ResultText = "Hình ảnh nhu mô não bình thường",
            Conclusion = "Chưa ghi nhận bất thường nội sọ",
            IsFinalized = true,
            VerifiedByStaffId = null,
            EnteredAt = VietnamNow(),
        };
        Db.DiagnosticResultVersions.AddRange(
            new DiagnosticResultVersion
            {
                DiagnosticResultId = conflictRes.Id,
                VersionNo = 1,
                ResultText = "Secret Other Patient PHI",
                Conclusion = "Secret",
                IsFinalized = true,
                VerifiedByStaffId = staffId,
                VerifiedAt = VietnamNow(),
            },
            new DiagnosticResultVersion
            {
                DiagnosticResultId = cancelledRes.Id,
                VersionNo = 1,
                ResultText = "Cancelled Visit Result",
                Conclusion = "Cancelled",
                IsFinalized = true,
                VerifiedByStaffId = staffId,
                VerifiedAt = VietnamNow(),
            },
            gatedVersion);

        var conflictRx = new Prescription
        {
            PrescriptionCode = $"RX-CNF-{suffix}".ToUpperInvariant(),
            VisitId = conflictingVisit.Id,
            Status = "ISSUED",
            IssuedAt = VietnamNow(),
            CreatedAt = VietnamNow(),
        };
        Db.Prescriptions.Add(conflictRx);
        await Db.SaveChangesAsync();

        // Conflicting visit.PatientId != appointment.PatientId MUST return 404 on both DiagnosticResults and Prescriptions + write conflict audit!
        using (var cnfDiagReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/patients/me/diagnostic-results/{conflictOrder.OrderCode}"))
        {
            cnfDiagReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerTokens.AccessToken);
            var cnfDiagRes = await Client.SendAsync(cnfDiagReq);
            Assert.Equal(HttpStatusCode.NotFound, cnfDiagRes.StatusCode);
        }

        using (var cnfRxReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/patients/me/prescriptions/{conflictRx.PrescriptionCode}"))
        {
            cnfRxReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerTokens.AccessToken);
            var cnfRxRes = await Client.SendAsync(cnfRxReq);
            Assert.Equal(HttpStatusCode.NotFound, cnfRxRes.StatusCode);
        }

        // CANCELLED visit diagnostic result is blocked (404)
        using (var canDiagReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/patients/me/diagnostic-results/{cancelledOrder.OrderCode}"))
        {
            canDiagReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerTokens.AccessToken);
            var canDiagRes = await Client.SendAsync(canDiagReq);
            Assert.Equal(HttpStatusCode.NotFound, canDiagRes.StatusCode);
        }

        // RequiresResultVerification == true while unverified -> isPublishedToPatient == false (redacted ResultText/Conclusion)
        using (var unverifiedReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/patients/me/diagnostic-results/{gatedOrder.OrderCode}"))
        {
            unverifiedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerTokens.AccessToken);
            var unverifiedRes = await Client.SendAsync(unverifiedReq);
            Assert.Equal(HttpStatusCode.OK, unverifiedRes.StatusCode);
            var sheet = await unverifiedRes.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(sheet.GetProperty("isPublishedToPatient").GetBoolean());
            Assert.Equal(JsonValueKind.Null, sheet.GetProperty("resultText").ValueKind);
        }

        // Mark verified -> now published + PHI AuditLog recorded!
        await Db.DiagnosticResults.Where(r => r.Id == gatedRes.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "VERIFIED"));
        await Db.DiagnosticResultVersions.Where(v => v.Id == gatedVersion.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.VerifiedByStaffId, staffId)
                .SetProperty(v => v.VerifiedAt, VietnamNow()));

        using (var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/diagnostic-results"))
        {
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerTokens.AccessToken);
            var listRes = await Client.SendAsync(listReq);
            Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        }

        using (var verifiedReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/patients/me/diagnostic-results/{gatedOrder.OrderCode}"))
        {
            verifiedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerTokens.AccessToken);
            var verifiedRes = await Client.SendAsync(verifiedReq);
            Assert.Equal(HttpStatusCode.OK, verifiedRes.StatusCode);
            var sheet = await verifiedRes.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(sheet.GetProperty("isPublishedToPatient").GetBoolean());
            Assert.Contains("nhu mô não", sheet.GetProperty("resultText").GetString(), StringComparison.OrdinalIgnoreCase);
        }

        Db.ChangeTracker.Clear();
        var phiAudits = await Db.AuditLogs.AsNoTracking()
            .Where(a => a.ActorUserId == callerUser.Id)
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Contains(phiAudits, a => a.Contains("DIAGNOSTIC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(phiAudits, a => a.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase));

        // Finally, LockedUntil > UtcNow blocks PatientSelfResolver immediately (401 Unauthorized)!
        await Db.Users.Where(u => u.Id == callerUser.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockedUntil, DateTime.UtcNow.AddMinutes(15)));

        using (var lockedDiagReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/diagnostic-results"))
        {
            lockedDiagReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerTokens.AccessToken);
            var lockedDiagRes = await Client.SendAsync(lockedDiagReq);
            Assert.Equal(HttpStatusCode.Unauthorized, lockedDiagRes.StatusCode);
        }

        await CleanupPhase02DiagnosticArtifactsAsync();
    }
}
