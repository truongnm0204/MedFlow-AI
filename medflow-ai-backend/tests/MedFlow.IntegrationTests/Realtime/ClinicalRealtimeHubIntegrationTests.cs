using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using MedFlow.API.Realtime;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Entities;
using MedFlow.Infrastructure.BackgroundJobs;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace MedFlow.IntegrationTests.Realtime;

public sealed class ClinicalRealtimeHubIntegrationTests : AuthTestBase
{
    private const string HubPath = "/hubs/clinical";
    private const string WebhookUrl = "/api/v1/payments/sepay/webhook";
    private const string SePayApiKey = "IntegrationTest_SePay_Key";

    private readonly ITestOutputHelper _output;

    public ClinicalRealtimeHubIntegrationTests(
        TestWebAppFactory factory,
        ITestOutputHelper output)
        : base(factory)
    {
        _output = output;
    }

    [Fact]
    public void ProductionDi_ResolvesRealSignalRPublisherAndRevoker()
    {
        using var scope = Factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IRealtimeNotificationPublisher>();
        var revoker = scope.ServiceProvider.GetRequiredService<IRealtimeAccessRevoker>();

        Assert.IsType<SignalRRealtimeNotificationPublisher>(publisher);
        Assert.IsType<SignalRRealtimeAccessRevoker>(revoker);
    }

    [Fact]
    public async Task Handshake_AuthN_QueryStringScope_And_ZeroJwtLogLeakage()
    {
        var validPatientToken = GenerateToken(1, "patient01@medflow.test", "PATIENT");
        var tamperedToken = validPatientToken[..^4] + "XXXX";

        // 1. Anonymous connection to /hubs/clinical -> 401 Unauthorized
        await using (var anonConn = CreateHubConnection(Factory, accessToken: null, useWebSockets: false))
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => anonConn.StartAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        }

        // 2. Tampered JWT connection to /hubs/clinical -> 401 Unauthorized
        await using (var badConn = CreateHubConnection(Factory, accessToken: tamperedToken, useWebSockets: false))
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => badConn.StartAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        }

        // 3. ?access_token= query string MUST NOT authenticate regular REST API routes
        using var meResponse = await Client.GetAsync($"/api/v1/auth/me?access_token={Uri.EscapeDataString(validPatientToken)}");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        // 4. WebSocket handshake with ?access_token=<jwt> succeeds AND writes zero raw JWT characters to logs
        var logSink = new CapturingLoggerProvider();
        await using var loggingFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logSink));
        });
        _ = loggingFactory.CreateClient();

        string scopedToken;
        using (var scope = loggingFactory.Services.CreateScope())
        {
            var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            scopedToken = jwt.GenerateAccessToken(new User { Id = 5, Email = "reception01@medflow.test" }, ["RECEPTION"]).AccessToken;
        }

        await using (var wsConn = CreateHubConnection(loggingFactory, scopedToken, useWebSockets: true, origin: "http://localhost:5173"))
        {
            await wsConn.StartAsync();
            Assert.Equal(HubConnectionState.Connected, wsConn.State);

            var ack = await wsConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinHospitalQueueGroup");
            Assert.Equal("hospital-queue:1", ack.ResourceKey);
            await wsConn.StopAsync();
        }

        Assert.DoesNotContain(
            logSink.Entries,
            entry => entry.Contains(scopedToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebSocketOriginAllowlist_BlocksDisallowedOrigin_AndAllowsFrontendOrigin()
    {
        var receptionToken = GenerateToken(5, "reception01@medflow.test", "RECEPTION");

        // Disallowed Origin -> rejected during OnConnectedAsync
        await using (var evilConn = CreateHubConnection(
            Factory,
            receptionToken,
            useWebSockets: true,
            origin: "https://evil-attacker.example.com"))
        {
            var rejected = false;
            try
            {
                await evilConn.StartAsync();
                await evilConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinHospitalQueueGroup");
            }
            catch
            {
                rejected = true;
            }

            Assert.True(rejected || evilConn.State != HubConnectionState.Connected);
        }

        // Allowed Frontend Origin -> succeeds
        await using (var goodConn = CreateHubConnection(
            Factory,
            receptionToken,
            useWebSockets: true,
            origin: "http://localhost:5173"))
        {
            await goodConn.StartAsync();
            var ack = await goodConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinHospitalQueueGroup");
            Assert.Equal("hospital-queue:1", ack.ResourceKey);
        }
    }

    [Fact]
    public void RegistryLimitsAndLeaseTtl_EnforcesGroupCapRateLimitAndPrunesExpiredSubscriptions()
    {
        var fakeClock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 25, 6, 0, 0, TimeSpan.Zero));
        var options = new StaticOptionsMonitor<RealtimeOptions>(new RealtimeOptions
        {
            MaxGroupsPerConnection = 20,
            MaxInvocationsPerMinute = 30,
            MembershipTtlMinutes = 5,
            MaxReceiveMessageSizeBytes = 8192,
            PublishTimeoutSeconds = 3,
        });

        var registry = new ClinicalRealtimeConnectionRegistry(options, fakeClock);
        registry.RegisterConnection("conn-limits", userId: 1);

        // 1. Up to 20 distinct groups allowed; 21st distinct group rejected with GROUP_LIMIT_EXCEEDED
        for (var i = 1; i <= 20; i++)
        {
            var grant = new AuthorizedRealtimeSubscription(
                ResourceKey: $"visit-payment:{i}",
                PrimaryGroupNames: [$"visit:{i}"],
                BucketGroupNames: [],
                HospitalId: 1,
                PatientId: 1,
                VisitId: i);
            var ok = registry.TryUpsertSubscription("conn-limits", 1, grant, i.ToString(CultureInfo.InvariantCulture));
            Assert.True(ok.Allowed);
        }

        var overflowGrant = new AuthorizedRealtimeSubscription(
            ResourceKey: "visit-payment:21",
            PrimaryGroupNames: ["visit:21"],
            BucketGroupNames: [],
            HospitalId: 1,
            PatientId: 1,
            VisitId: 21);
        var overflow = registry.TryUpsertSubscription("conn-limits", 1, overflowGrant, "21");
        Assert.False(overflow.Allowed);
        Assert.Equal("GROUP_LIMIT_EXCEEDED", overflow.ErrorCode);

        // 2. Sliding-window rate limit: 30 invocations/min allowed, 31st blocked with [RATE_LIMITED]
        for (var r = 1; r <= 30; r++)
        {
            Assert.True(registry.TryCheckInvocationRateLimit("conn-limits", out _));
        }

        Assert.False(registry.TryCheckInvocationRateLimit("conn-limits", out var rateError));
        Assert.Contains("[RATE_LIMITED]", rateError, StringComparison.Ordinal);

        // 3. Advance clock > 5 minutes -> PruneAllExpiredSubscriptions evicts all 20 expired leases
        fakeClock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        var pruned = registry.PruneAllExpiredSubscriptions();
        Assert.Equal(20, pruned.Count);
    }

    [Fact]
    public async Task SePayWebhook_EndToEnd_EnforcesTwoLayerAuthZ_ZeroPhiWirePayload_Idempotency_AndActiveFamilyRevocation()
    {
        if (!await Db.Database.CanConnectAsync())
            return;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var room = new HospitalRoom
        {
            HospitalId = 1,
            DepartmentId = 2,
            RoomCode = $"RT{suffix}"[..8].ToUpperInvariant(),
            Name = $"Phong Realtime Payment {suffix}",
            RoomType = "EXAMINATION",
            Capacity = 4,
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);

        var policy = new HospitalPolicy
        {
            HospitalId = 1,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN DEMO"}""",
            PolicyVersion = 1,
        };
        Db.HospitalPolicies.Add(policy);

        var memberPatient = new Patient
        {
            PatientCode = $"PAT-FAM-{suffix}".ToUpperInvariant(),
            FullName = "Nguyễn Thị Người Thân",
            DateOfBirth = new DateOnly(2015, 5, 10),
            Gender = "FEMALE",
            Address = "123 Lê Lợi, Quận 1",
            Status = "ACTIVE",
        };
        Db.Patients.Add(memberPatient);
        await Db.SaveChangesAsync();

        var familyLink = new PatientFamilyLink
        {
            OwnerPatientId = 1,
            MemberPatientId = memberPatient.Id,
            Relationship = "Con",
            VerificationStatus = "VERIFIED",
            VerificationMethod = "OTP",
            VerifiedAt = VietnamNow(),
        };
        Db.PatientFamilyLinks.Add(familyLink);

        var legacyMemberPatient = new Patient
        {
            PatientCode = $"PAT-LEG-{suffix}".ToUpperInvariant(),
            FullName = "Trần Văn Legacy",
            DateOfBirth = new DateOnly(1980, 3, 20),
            Gender = "MALE",
            Address = "456 Nguyễn Huệ, Quận 1",
            Status = "ACTIVE",
        };
        Db.Patients.Add(legacyMemberPatient);
        await Db.SaveChangesAsync();

        var legacyLink = new PatientFamilyLink
        {
            OwnerPatientId = 1,
            MemberPatientId = legacyMemberPatient.Id,
            Relationship = "Anh",
            VerificationStatus = "LEGACY",
            LegacyAccessUntil = VietnamNow().AddDays(7),
        };
        Db.PatientFamilyLinks.Add(legacyLink);

        var ownerVisit = new Visit
        {
            VisitCode = $"VIS-OWN-{suffix}".ToUpperInvariant(),
            PatientId = 1,
            ExaminationRoomId = room.Id,
            Status = "CHECKED_IN",
            CheckedInAt = VietnamNow(),
        };
        var familyVisit = new Visit
        {
            VisitCode = $"VIS-FAM-{suffix}".ToUpperInvariant(),
            PatientId = memberPatient.Id,
            ExaminationRoomId = room.Id,
            Status = "CHECKED_IN",
            CheckedInAt = VietnamNow(),
        };
        var legacyVisit = new Visit
        {
            VisitCode = $"VIS-LEG-{suffix}".ToUpperInvariant(),
            PatientId = legacyMemberPatient.Id,
            ExaminationRoomId = room.Id,
            Status = "CHECKED_IN",
            CheckedInAt = VietnamNow(),
        };
        Db.Visits.AddRange(ownerVisit, familyVisit, legacyVisit);
        await Db.SaveChangesAsync();

        Db.VisitCharges.AddRange(
            new VisitCharge { VisitId = ownerVisit.Id, UnitPrice = 150_000m, PaymentRequirement = "REQUIRED" },
            new VisitCharge { VisitId = familyVisit.Id, UnitPrice = 200_000m, PaymentRequirement = "REQUIRED" });

        var ownerPayment = new Payment
        {
            VisitId = ownerVisit.Id,
            Amount = 150_000m,
            Status = PaymentStatuses.Pending,
            CreatedAt = VietnamNow(),
        };
        var familyPayment = new Payment
        {
            VisitId = familyVisit.Id,
            Amount = 200_000m,
            Status = PaymentStatuses.Pending,
            CreatedAt = VietnamNow(),
        };
        Db.Payments.AddRange(ownerPayment, familyPayment);
        await Db.SaveChangesAsync();

        ownerPayment.PaymentCode = $"MF{ownerPayment.Id}";
        familyPayment.PaymentCode = $"MF{familyPayment.Id}";
        await Db.SaveChangesAsync();

        var gatewayTxId1 = Random.Shared.NextInt64(900_000, 999_999);
        var gatewayTxId2 = gatewayTxId1 + 1;

        try
        {
            var (_, unrelatedTokens) = await RegisterVerifyLoginAsync();
            var ownerToken = GenerateToken(1, "patient01@medflow.test", "PATIENT");
            var doctorToken = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

            await using var unrelatedConn = CreateHubConnection(Factory, unrelatedTokens.AccessToken, useWebSockets: true);
            await using var doctorConn = CreateHubConnection(Factory, doctorToken, useWebSockets: true);
            await using var ownerConn = CreateHubConnection(Factory, ownerToken, useWebSockets: true);

            var paymentChannel = Channel.CreateUnbounded<JsonElement>();
            var revokedChannel = Channel.CreateUnbounded<JsonElement>();
            ownerConn.On<JsonElement>("VisitPaymentChanged", payload => paymentChannel.Writer.TryWrite(payload.Clone()));
            ownerConn.On<JsonElement>("MembershipRevoked", payload => revokedChannel.Writer.TryWrite(payload.Clone()));

            await Task.WhenAll(unrelatedConn.StartAsync(), doctorConn.StartAsync(), ownerConn.StartAsync());

            // 1. Two-layer AuthZ denials: unrelated patient, DOCTOR without PAYMENT_VIEW, and LEGACY family link
            var unrelatedEx = await Assert.ThrowsAsync<HubException>(
                () => unrelatedConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", ownerVisit.VisitCode));
            Assert.Contains("[FORBIDDEN]", unrelatedEx.Message, StringComparison.Ordinal);

            var doctorEx = await Assert.ThrowsAsync<HubException>(
                () => doctorConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", ownerVisit.VisitCode));
            Assert.Contains("[FORBIDDEN]", doctorEx.Message, StringComparison.Ordinal);

            var legacyEx = await Assert.ThrowsAsync<HubException>(
                () => ownerConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", legacyVisit.VisitCode));
            Assert.Contains("[FORBIDDEN]", legacyEx.Message, StringComparison.Ordinal);

            // 2. Owner joins own visit by VisitCode -> normalized to canonical visit-payment:{visitId}
            var ownAck = await ownerConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", ownerVisit.VisitCode);
            Assert.Equal($"visit-payment:{ownerVisit.Id}", ownAck.ResourceKey);

            // 3. SePay PAID webhook emits minimal Zero-PHI VisitPaymentChanged payload once
            using (var webhookRes = await PostSePayWebhookAsync(gatewayTxId1, 150_000m, ownerPayment.PaymentCode!))
            {
                Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);
            }

            var wirePayload = await ReadWithTimeoutAsync(paymentChannel.Reader, TimeSpan.FromSeconds(3));
            var keys = wirePayload.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(["eventType", "visitId"], keys);
            Assert.Equal("VisitPaymentChanged", wirePayload.GetProperty("eventType").GetString());
            Assert.Equal(ownerVisit.Id, wirePayload.GetProperty("visitId").GetInt64());

            // 4. Duplicate SePay webhook (same gateway id) does NOT emit a second SignalR event
            using (var dupRes = await PostSePayWebhookAsync(gatewayTxId1, 150_000m, ownerPayment.PaymentCode!))
            {
                Assert.Equal(HttpStatusCode.OK, dupRes.StatusCode);
            }

            await AssertNoEventAsync(paymentChannel.Reader, TimeSpan.FromMilliseconds(200));

            // 5. Active Family Link Revocation: owner joins OTP-verified family visit, then removes family link
            var famAck = await ownerConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinVisitPaymentGroup", familyVisit.VisitCode);
            Assert.Equal($"visit-payment:{familyVisit.Id}", famAck.ResourceKey);

            using (var scope = Factory.Services.CreateScope())
            {
                var familyService = scope.ServiceProvider.GetRequiredService<IFamilyProfileService>();
                await familyService.RemoveFamilyMemberAsync(
                    userId: 1,
                    identifier: memberPatient.Id.ToString(CultureInfo.InvariantCulture),
                    reason: "Thu hồi quyền xem hồ sơ gia đình");
            }

            var revokedPayload = await ReadWithTimeoutAsync(revokedChannel.Reader, TimeSpan.FromSeconds(3));
            Assert.Equal($"visit-payment:{familyVisit.Id}", revokedPayload.GetProperty("resourceKey").GetString());
            Assert.Equal("FAMILY_LINK_REVOKED", revokedPayload.GetProperty("reasonCode").GetString());

            // Subsequent SePay PAID webhook on familyVisit MUST NOT reach the evicted connection
            using (var famWebhookRes = await PostSePayWebhookAsync(gatewayTxId2, 200_000m, familyPayment.PaymentCode!))
            {
                Assert.Equal(HttpStatusCode.OK, famWebhookRes.StatusCode);
            }

            await AssertNoEventAsync(paymentChannel.Reader, TimeSpan.FromMilliseconds(250));
        }
        finally
        {
            Db.ChangeTracker.Clear();
            var visitIds = new[] { ownerVisit.Id, familyVisit.Id, legacyVisit.Id };
            var paymentIds = new[] { ownerPayment.Id, familyPayment.Id };
            await Db.PaymentGatewayEvents.Where(e => e.PaymentId != null && paymentIds.Contains(e.PaymentId.Value)).ExecuteDeleteAsync();
            await Db.Payments.Where(p => p.VisitId != null && visitIds.Contains(p.VisitId.Value)).ExecuteDeleteAsync();
            await Db.VisitCharges.Where(c => c.VisitId != null && visitIds.Contains(c.VisitId.Value)).ExecuteDeleteAsync();
            var entityIdStrings = paymentIds.Concat(visitIds).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            await Db.AuditLogs.Where(a => a.EntityId != null && entityIdStrings.Contains(a.EntityId)).ExecuteDeleteAsync();
            await Db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
            await Db.PatientFamilyLinks.Where(l => l.MemberPatientId == memberPatient.Id || l.MemberPatientId == legacyMemberPatient.Id).ExecuteDeleteAsync();
            await Db.Patients.Where(p => p.Id == memberPatient.Id || p.Id == legacyMemberPatient.Id).ExecuteDeleteAsync();
            await Db.HospitalPolicies.Where(p => p.Id == policy.Id).ExecuteDeleteAsync();
            await Db.HospitalRooms.Where(r => r.Id == room.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task QueueCallNext_RoomTransferBucketMigration_AndLifecycleNoShow_EndToEnd()
    {
        if (!await Db.Database.CanConnectAsync())
            return;

        var vnNow = VietnamNow();
        var today = DateOnly.FromDateTime(vnNow);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var roomA = new HospitalRoom
        {
            HospitalId = 1,
            DepartmentId = 2,
            RoomCode = $"RTA{suffix}"[..9],
            Name = $"Phòng Realtime A {suffix}",
            RoomType = "EXAMINATION",
            Capacity = 5,
            Status = "ACTIVE",
        };
        var roomB = new HospitalRoom
        {
            HospitalId = 1,
            DepartmentId = 2,
            RoomCode = $"RTB{suffix}"[..9],
            Name = $"Phòng Realtime B {suffix}",
            RoomType = "EXAMINATION",
            Capacity = 5,
            Status = "ACTIVE",
        };
        Db.HospitalRooms.AddRange(roomA, roomB);
        await Db.SaveChangesAsync();

        var spA = new QueueServicePoint
        {
            HospitalId = 1,
            RoomId = roomA.Id,
            DoctorId = 1,
            PointType = "ROOM",
            Name = $"Điểm phục vụ Realtime A {suffix}",
            IsActive = true,
        };
        var spB = new QueueServicePoint
        {
            HospitalId = 1,
            RoomId = roomB.Id,
            PointType = "ROOM",
            Name = $"Điểm phục vụ Realtime B {suffix}",
            IsActive = true,
        };
        Db.QueueServicePoints.AddRange(spA, spB);
        await Db.SaveChangesAsync();

        var activeSlot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = roomA.Id,
            StartsAt = vnNow.AddMinutes(-15),
            EndsAt = vnNow.AddMinutes(45),
            Capacity = 5,
            Status = "OPEN",
        };
        var overdueSlot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = roomA.Id,
            StartsAt = vnNow.AddHours(-5),
            EndsAt = vnNow.AddHours(-4),
            Capacity = 5,
            Status = "OPEN",
        };
        Db.ExaminationSlots.AddRange(activeSlot, overdueSlot);
        await Db.SaveChangesAsync();

        var trackedAppt = new Appointment
        {
            PatientId = 1,
            BookedByUserId = 1,
            SlotId = activeSlot.Id,
            AppointmentCode = $"APT-RTA-{suffix}",
            BookingChannel = "ONLINE",
            Status = "CHECKED_IN",
            BookedAt = vnNow.AddHours(-2),
        };
        var overdueAppt = new Appointment
        {
            PatientId = 1,
            BookedByUserId = 1,
            SlotId = overdueSlot.Id,
            AppointmentCode = $"APT-NS-{suffix}",
            BookingChannel = "ONLINE",
            Status = "BOOKED",
            BookedAt = vnNow.AddHours(-6),
        };
        Db.Appointments.AddRange(trackedAppt, overdueAppt);
        await Db.SaveChangesAsync();

        var trackedVisit = new Visit
        {
            VisitCode = $"VIS-TRK-{suffix}",
            PatientId = 1,
            AppointmentId = trackedAppt.Id,
            AssignedDoctorId = 1,
            ExaminationRoomId = roomA.Id,
            CheckedInByStaffId = 3,
            Status = "WAITING",
            CheckedInAt = vnNow.AddMinutes(-12),
        };
        var roomACompanionVisit = new Visit
        {
            VisitCode = $"VIS-CPA-{suffix}",
            PatientId = 4,
            AssignedDoctorId = 1,
            ExaminationRoomId = roomA.Id,
            CheckedInByStaffId = 3,
            Status = "WAITING",
            CheckedInAt = vnNow.AddMinutes(-10),
        };
        var roomBCompanionVisit = new Visit
        {
            VisitCode = $"VIS-CPB-{suffix}",
            PatientId = 4,
            AssignedDoctorId = 1,
            ExaminationRoomId = roomB.Id,
            CheckedInByStaffId = 3,
            Status = "WAITING",
            CheckedInAt = vnNow.AddMinutes(-8),
        };
        Db.Visits.AddRange(trackedVisit, roomACompanionVisit, roomBCompanionVisit);
        await Db.SaveChangesAsync();

        Db.VisitCharges.AddRange(
            VisitCharge.CreateExamination(trackedVisit.Id, serviceId: 1, unitPrice: 0m, createdAtLocal: vnNow, waived: true),
            VisitCharge.CreateExamination(roomACompanionVisit.Id, serviceId: 1, unitPrice: 0m, createdAtLocal: vnNow, waived: true),
            VisitCharge.CreateExamination(roomBCompanionVisit.Id, serviceId: 1, unitPrice: 0m, createdAtLocal: vnNow, waived: true));
        await Db.SaveChangesAsync();

        var trackedQueue = new QueueEntry
        {
            VisitId = trackedVisit.Id,
            AppointmentId = trackedAppt.Id,
            ServicePointId = spA.Id,
            CurrentServicePointId = spA.Id,
            QueueDate = today,
            QueueNumber = 1,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            ArrivalClass = "ON_TIME",
            ManualPriority = "ELDERLY",
            StateVersion = 1,
            ArrivedAt = vnNow.AddMinutes(-12),
            ReadyAt = vnNow.AddMinutes(-12),
        };
        var roomACompanionQueue = new QueueEntry
        {
            VisitId = roomACompanionVisit.Id,
            ServicePointId = spA.Id,
            CurrentServicePointId = spA.Id,
            QueueDate = today,
            QueueNumber = 2,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            StateVersion = 1,
            ArrivedAt = vnNow.AddMinutes(-10),
            ReadyAt = vnNow.AddMinutes(-10),
        };
        var roomBCompanionQueue = new QueueEntry
        {
            VisitId = roomBCompanionVisit.Id,
            ServicePointId = spB.Id,
            CurrentServicePointId = spB.Id,
            QueueDate = today,
            QueueNumber = 1,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            StateVersion = 1,
            ArrivedAt = vnNow.AddMinutes(-8),
            ReadyAt = vnNow.AddMinutes(-8),
        };
        var overdueReservedQueue = new QueueEntry
        {
            AppointmentId = overdueAppt.Id,
            ServicePointId = spA.Id,
            CurrentServicePointId = spA.Id,
            QueueDate = DateOnly.FromDateTime(overdueSlot.StartsAt!.Value),
            QueueNumber = 9,
            QueueType = "EXAMINATION",
            Status = "RESERVED",
            PresenceStatus = "ABSENT",
            StateVersion = 1,
        };
        Db.QueueEntries.AddRange(trackedQueue, roomACompanionQueue, roomBCompanionQueue, overdueReservedQueue);
        await Db.SaveChangesAsync();

        try
        {
            var patientToken = GenerateToken(1, "patient01@medflow.test", "PATIENT");
            var receptionToken = GenerateToken(5, "reception01@medflow.test", "RECEPTION");

            await using var patientConn = CreateHubConnection(Factory, patientToken, useWebSockets: true);
            await using var receptionConn = CreateHubConnection(Factory, receptionToken, useWebSockets: true);

            var patientQueueChannel = Channel.CreateUnbounded<JsonElement>();
            var patientScopeChannel = Channel.CreateUnbounded<JsonElement>();
            var patientRevokedChannel = Channel.CreateUnbounded<JsonElement>();
            var receptionQueueChannel = Channel.CreateUnbounded<JsonElement>();

            patientConn.On<JsonElement>("QueueChanged", p => patientQueueChannel.Writer.TryWrite(p.Clone()));
            patientConn.On<JsonElement>("QueueScopeChanged", p => patientScopeChannel.Writer.TryWrite(p.Clone()));
            patientConn.On<JsonElement>("MembershipRevoked", p => patientRevokedChannel.Writer.TryWrite(p.Clone()));
            receptionConn.On<JsonElement>("QueueChanged", p => receptionQueueChannel.Writer.TryWrite(p.Clone()));

            await Task.WhenAll(patientConn.StartAsync(), receptionConn.StartAsync());

            var patientAck = await patientConn.InvokeAsync<RealtimeSubscriptionAckDto>(
                "JoinAppointmentQueueGroup",
                trackedAppt.AppointmentCode);
            Assert.Equal($"appointment-queue:{trackedAppt.Id}", patientAck.ResourceKey);

            var overdueAck = await patientConn.InvokeAsync<RealtimeSubscriptionAckDto>(
                "JoinAppointmentQueueGroup",
                overdueAppt.AppointmentCode);
            Assert.Equal($"appointment-queue:{overdueAppt.Id}", overdueAck.ResourceKey);

            var receptionAck = await receptionConn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinHospitalQueueGroup");
            Assert.Equal("hospital-queue:1", receptionAck.ResourceKey);

            // 1. CallNext on SpA -> both Patient (in patient-bucket:sp:{spA}) and Reception receive minimal Zero-PHI QueueChanged
            using (var callNextRes = await SendAuthedPostAsync(
                $"/api/v1/queue/service-points/sp-{spA.Id}/call-next",
                new CallNextPatientRequest(Notes: "Realtime CallNext SpA"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, callNextRes.StatusCode);
            }

            var patientQueueEvt = await ReadWithTimeoutAsync(patientQueueChannel.Reader, TimeSpan.FromSeconds(3));
            var receptionQueueEvt = await ReadWithTimeoutAsync(receptionQueueChannel.Reader, TimeSpan.FromSeconds(3));

            var queueKeys = patientQueueEvt.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(["eventType", "servicePointId"], queueKeys);
            Assert.Equal("QueueChanged", patientQueueEvt.GetProperty("eventType").GetString());
            Assert.Equal(spA.Id, patientQueueEvt.GetProperty("servicePointId").GetInt64());
            Assert.Equal(spA.Id, receptionQueueEvt.GetProperty("servicePointId").GetInt64());

            DrainChannel(patientQueueChannel.Reader);
            DrainChannel(receptionQueueChannel.Reader);

            // 2. Transfer trackedVisit from Room A -> Room B: patient receives QueueScopeChanged and migrates buckets
            using (var transferRes = await SendAuthedPostAsync(
                $"/api/v1/visits/{trackedVisit.VisitCode}/transfer-room",
                new TransferVisitRoomRequest(TargetRoomId: roomB.Id, Reason: "Điều phối giảm tải sang phòng B"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, transferRes.StatusCode);
            }

            var scopeEvt = await ReadWithTimeoutAsync(patientScopeChannel.Reader, TimeSpan.FromSeconds(3));
            Assert.Equal(trackedAppt.Id, scopeEvt.GetProperty("appointmentId").GetInt64());

            // Simulate browser handleQueueScopeChanged + 4m lease renewal re-invoking JoinAppointmentQueueGroup
            var rejoinAck = await patientConn.InvokeAsync<RealtimeSubscriptionAckDto>(
                "JoinAppointmentQueueGroup",
                trackedAppt.AppointmentCode);
            Assert.Equal($"appointment-queue:{trackedAppt.Id}", rejoinAck.ResourceKey);

            // Wait briefly for transfer's own QueueChanged signals to settle, then drain
            await Task.Delay(150);
            DrainChannel(patientQueueChannel.Reader);
            DrainChannel(receptionQueueChannel.Reader);

            // First evict overdueAppt before testing Room A isolation so overdueAppt's SpA bucket doesn't match!
            using (var cronScope = Factory.Services.CreateScope())
            {
                var processor = cronScope.ServiceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                    .OfType<AppointmentLifecycleProcessor>()
                    .First();
                var summary = await processor.ProcessCycleAsync();
                Assert.True(summary.AutoNoShowCount >= 1);
            }

            var noShowRevoked = await ReadWithTimeoutAsync(patientRevokedChannel.Reader, TimeSpan.FromSeconds(3));
            Assert.Equal($"appointment-queue:{overdueAppt.Id}", noShowRevoked.GetProperty("resourceKey").GetString());
            Assert.Equal("APPOINTMENT_INACTIVE", noShowRevoked.GetProperty("reasonCode").GetString());

            var noShowHospitalEvt = await ReadWithTimeoutAsync(receptionQueueChannel.Reader, TimeSpan.FromSeconds(3));
            Assert.Equal("QueueChanged", noShowHospitalEvt.GetProperty("eventType").GetString());

            await Task.Delay(100);
            DrainChannel(patientQueueChannel.Reader);
            DrainChannel(receptionQueueChannel.Reader);

            // 3. CallNext on Room A's companion visit -> Patient (now migrated to Room B) MUST NOT receive Room A bucket signal!
            using (var callNextRoomARes = await SendAuthedPostAsync(
                $"/api/v1/queue/service-points/sp-{spA.Id}/call-next",
                new CallNextPatientRequest(Notes: "Call companion at Room A"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, callNextRoomARes.StatusCode);
            }

            _ = await ReadWithTimeoutAsync(receptionQueueChannel.Reader, TimeSpan.FromSeconds(3));
            await AssertNoEventAsync(patientQueueChannel.Reader, TimeSpan.FromMilliseconds(250));

            // 4. CallNext on Room B's companion visit -> Patient (now in patient-bucket:sp:{spB}) DOES receive Room B signal!
            using (var callNextRoomBRes = await SendAuthedPostAsync(
                $"/api/v1/queue/service-points/sp-{spB.Id}/call-next",
                new CallNextPatientRequest(Notes: "Call companion at Room B"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, callNextRoomBRes.StatusCode);
            }

            var migratedEvt = await ReadWithTimeoutAsync(patientQueueChannel.Reader, TimeSpan.FromSeconds(3));
            Assert.Equal("QueueChanged", migratedEvt.GetProperty("eventType").GetString());
            Assert.Equal(spB.Id, migratedEvt.GetProperty("servicePointId").GetInt64());
        }
        finally
        {
            Db.ChangeTracker.Clear();
            var spIds = new[] { spA.Id, spB.Id };
            var visitIds = new[] { trackedVisit.Id, roomACompanionVisit.Id, roomBCompanionVisit.Id };
            var apptIds = new[] { trackedAppt.Id, overdueAppt.Id };
            var slotIds = new[] { activeSlot.Id, overdueSlot.Id };
            var roomIds = new[] { roomA.Id, roomB.Id };
            var queueIds = new[] { trackedQueue.Id, roomACompanionQueue.Id, roomBCompanionQueue.Id, overdueReservedQueue.Id };

            await Db.Notifications.Where(n => n.AppointmentId != null && apptIds.Contains(n.AppointmentId.Value)).ExecuteDeleteAsync();
            await Db.QueueFairnessStates.Where(s => spIds.Contains(s.ServicePointId)).ExecuteDeleteAsync();
            // #863: payment_enrollment_intents.queue_entry_id + clinical_session_recovery_requests.queue_entry_id
            // are ON DELETE RESTRICT; intent rows now exist for auto-enrolled CLS orders, so they must go first.
            await Db.PaymentEnrollmentIntents
                .Where(i => i.QueueEntryId.HasValue && queueIds.Contains(i.QueueEntryId.Value))
                .ExecuteDeleteAsync();
            await Db.ClinicalSessionRecoveryRequests
                .Where(r => queueIds.Contains(r.QueueEntryId))
                .ExecuteDeleteAsync();
            await Db.QueueEntries.Where(q => queueIds.Contains(q.Id)).ExecuteDeleteAsync();

            var entityIds = queueIds.Concat(visitIds).Concat(apptIds)
                .Select(id => id.ToString(CultureInfo.InvariantCulture))
                .ToArray();
            await Db.AuditLogs.Where(a => a.EntityId != null && entityIds.Contains(a.EntityId)).ExecuteDeleteAsync();

            await Db.VisitCharges.Where(c => c.VisitId != null && visitIds.Contains(c.VisitId.Value)).ExecuteDeleteAsync();
            await Db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
            await Db.Appointments.Where(a => apptIds.Contains(a.Id)).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => slotIds.Contains(s.Id)).ExecuteDeleteAsync();
            await Db.QueueServicePoints.Where(sp => spIds.Contains(sp.Id)).ExecuteDeleteAsync();
            await Db.HospitalRooms.Where(r => roomIds.Contains(r.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task RealtimeDeliveryLatencyBenchmark_MeasuresEmpiricalP50AndP95OverWebSockets()
    {
        var receptionToken = GenerateToken(5, "reception01@medflow.test", "RECEPTION");
        await using var conn = CreateHubConnection(Factory, receptionToken, useWebSockets: true, origin: "http://localhost:5173");

        TaskCompletionSource<JsonElement>? pendingTcs = null;
        conn.On<JsonElement>("QueueChanged", payload =>
        {
            Volatile.Read(ref pendingTcs)?.TrySetResult(payload.Clone());
        });

        await conn.StartAsync();
        await conn.InvokeAsync<RealtimeSubscriptionAckDto>("JoinHospitalQueueGroup");

        using var scope = Factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IRealtimeNotificationPublisher>();
        var notification = new QueueChangedNotification(HospitalId: 1, ServicePointId: 1, RoomId: 1);

        // Warmup (5 iterations)
        for (var w = 0; w < 5; w++)
        {
            var warmupTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref pendingTcs, warmupTcs);
            await publisher.PublishQueueChangedAsync(notification);
            await warmupTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }

        const int sampleCount = 40;
        var latenciesMs = new double[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref pendingTcs, tcs);

            var sw = Stopwatch.StartNew();
            await publisher.PublishQueueChangedAsync(notification);
            _ = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            sw.Stop();

            latenciesMs[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(latenciesMs);
        var p50 = Percentile(latenciesMs, 0.50);
        var p95 = Percentile(latenciesMs, 0.95);
        var max = latenciesMs[^1];

        _output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[BENCHMARK] SignalR WebSocket QueueChanged (broker hop) N={sampleCount}: P50={p50:F2}ms, P95={p95:F2}ms, Max={max:F2}ms"));

        Assert.True(p95 < 250.0, $"Expected in-process TestServer WebSocket P95 < 250ms, actual P95={p95:F2}ms");

        if (await Db.Database.CanConnectAsync())
        {
            const int dbSampleCount = 20;
            var dbLatenciesMs = new double[dbSampleCount];
            var createdAuditIds = new List<long>(dbSampleCount);

            try
            {
                for (var i = 0; i < dbSampleCount; i++)
                {
                    var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Volatile.Write(ref pendingTcs, tcs);

                    var sw = Stopwatch.StartNew();
                    var audit = new AuditLog
                    {
                        ActorUserId = 5,
                        HospitalId = 1,
                        Action = "REALTIME_BENCHMARK_COMMIT",
                        EntityType = "QUEUE_BENCHMARK",
                        EntityId = i.ToString(CultureInfo.InvariantCulture),
                        OccurredAt = DateTime.UtcNow,
                    };
                    Db.AuditLogs.Add(audit);
                    await Db.SaveChangesAsync();
                    createdAuditIds.Add(audit.Id);

                    await publisher.PublishQueueChangedAsync(notification);
                    _ = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    sw.Stop();

                    dbLatenciesMs[i] = sw.Elapsed.TotalMilliseconds;
                }

                Array.Sort(dbLatenciesMs);
                var dbP50 = Percentile(dbLatenciesMs, 0.50);
                var dbP95 = Percentile(dbLatenciesMs, 0.95);
                var dbMax = dbLatenciesMs[^1];

                _output.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"[BENCHMARK] PostgreSQL SaveChanges + SignalR WebSocket delivery N={dbSampleCount}: P50={dbP50:F2}ms, P95={dbP95:F2}ms, Max={dbMax:F2}ms"));

                Assert.True(dbP95 < 300.0, $"Expected PostgreSQL + WebSocket P95 < 300ms, actual P95={dbP95:F2}ms");
            }
            finally
            {
                if (createdAuditIds.Count > 0)
                {
                    await Db.AuditLogs.Where(a => createdAuditIds.Contains(a.Id)).ExecuteDeleteAsync();
                }
            }
        }
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(new User { Id = userId, Email = email }, roles).AccessToken;
    }

    private static HubConnection CreateHubConnection(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        string? accessToken,
        bool useWebSockets,
        string? origin = null)
    {
        var hubUri = new Uri(factory.Server.BaseAddress, HubPath);
        return new HubConnectionBuilder()
            .WithUrl(
                hubUri,
                options =>
                {
                    if (accessToken is not null)
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    }

                    if (!string.IsNullOrWhiteSpace(origin))
                    {
                        options.Headers["Origin"] = origin;
                    }

                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();

                    if (useWebSockets)
                    {
                        options.Transports = HttpTransportType.WebSockets;
                        options.WebSocketFactory = async (context, cancellationToken) =>
                        {
                            var wsClient = factory.Server.CreateWebSocketClient();
                            wsClient.ConfigureRequest = req =>
                            {
                                if (!string.IsNullOrWhiteSpace(origin))
                                {
                                    req.Headers.Origin = origin;
                                }
                            };

                            var targetUri = context.Uri;
                            if (!string.IsNullOrWhiteSpace(accessToken) &&
                                !targetUri.Query.Contains("access_token=", StringComparison.OrdinalIgnoreCase))
                            {
                                var separator = string.IsNullOrEmpty(targetUri.Query) ? "?" : "&";
                                targetUri = new Uri($"{targetUri}{separator}access_token={Uri.EscapeDataString(accessToken)}");
                            }

                            return await wsClient.ConnectAsync(targetUri, cancellationToken);
                        };
                    }
                    else
                    {
                        options.Transports = HttpTransportType.LongPolling;
                    }
                })
            .Build();
    }

    private async Task<HttpResponseMessage> PostSePayWebhookAsync(long id, decimal amount, string content)
    {
        var payload = new
        {
            id,
            gateway = "Vietcombank",
            transactionDate = "2026-11-25 10:15:00",
            accountNumber = "0123456789",
            code = (string?)null,
            content,
            transferType = "in",
            transferAmount = amount,
            accumulated = amount,
            subAccount = (string?)null,
            referenceCode = $"FT-{id}",
        };

        var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Apikey", SePayApiKey);
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAuthedPostAsync<TBody>(string url, TBody body, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    private static async Task<T> ReadWithTimeoutAsync<T>(ChannelReader<T> reader, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await reader.ReadAsync(cts.Token);
    }

    private static async Task AssertNoEventAsync<T>(ChannelReader<T> reader, TimeSpan quietWindow)
    {
        using var cts = new CancellationTokenSource(quietWindow);
        try
        {
            var unexpected = await reader.ReadAsync(cts.Token);
            Assert.Fail($"Expected zero SignalR events within {quietWindow.TotalMilliseconds}ms, but received: {unexpected}");
        }
        catch (OperationCanceledException)
        {
            // Expected: channel remained quiet for the full window.
        }
    }

    private static void DrainChannel<T>(ChannelReader<T> reader)
    {
        while (reader.TryRead(out _))
        {
        }
    }

    private static double Percentile(double[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
            return 0;

        var index = (sortedSamples.Length - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
            return sortedSamples[lower];

        var weight = index - lower;
        return sortedSamples[lower] + ((sortedSamples[upper] - sortedSamples[lower]) * weight);
    }

    private static DateTime VietnamNow() =>
        DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;
        public TOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
