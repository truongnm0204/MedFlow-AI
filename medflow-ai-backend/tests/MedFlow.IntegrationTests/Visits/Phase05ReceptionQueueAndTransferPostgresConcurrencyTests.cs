using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Visits;

public sealed class Phase05ReceptionQueueAndTransferPostgresConcurrencyTests : AuthTestBase
{
    private const string WebhookUrl = "/api/v1/payments/sepay/webhook";
    private const string SePayApiKey = "IntegrationTest_SePay_Key";

    public Phase05ReceptionQueueAndTransferPostgresConcurrencyTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task ConcurrentWalkInsAndCheckInVsWalkIn_SamePatientSameDay_SerializesToSingleActiveVisitAndCharge()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        var vnNow = VietnamNow();
        var today = DateOnly.FromDateTime(vnNow);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var createdPatientIds = new List<long>();
        var createdRoomIds = new List<long>();
        var createdSpIds = new List<long>();
        var createdServiceIds = new List<long>();
        var createdSlotIds = new List<long>();
        var createdAppointmentIds = new List<long>();

        try
        {
            var svc = new Service
            {
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = $"P5S1-{suffix}",
                Name = $"Khám Tiêu hóa P05 {suffix}",
                ServiceType = "EXAMINATION",
                IsActive = true,
            };
            Db.Services.Add(svc);
            await Db.SaveChangesAsync();
            createdServiceIds.Add(svc.Id);

            Db.ServicePrices.Add(new ServicePrice
            {
                ServiceId = svc.Id,
                PriceAmount = 200_000m,
                EffectiveFrom = vnNow.AddDays(-10),
                EffectiveTo = null,
            });

            var room = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = $"P5R1-{suffix}"[..12],
                Name = $"Phòng P05-1 {suffix}",
                RoomType = "EXAMINATION",
                Capacity = 4,
                Status = "ACTIVE",
            };
            Db.HospitalRooms.Add(room);
            await Db.SaveChangesAsync();
            createdRoomIds.Add(room.Id);

            var sp = new QueueServicePoint
            {
                HospitalId = 1,
                RoomId = room.Id,
                DoctorId = 1,
                ServiceId = svc.Id,
                PointType = "DOCTOR",
                Name = $"SP P05-1 {suffix}",
                IsActive = true,
            };
            Db.QueueServicePoints.Add(sp);

            var patientA = new Patient
            {
                PatientCode = $"BN-P5A-{suffix}",
                FullName = "Nguyễn Tranh Chấp Vãng Lai",
                DateOfBirth = new DateOnly(1990, 4, 12),
                Gender = "MALE",
                Phone = "0901112233",
                Status = "ACTIVE",
            };
            var patientB = new Patient
            {
                PatientCode = $"BN-P5B-{suffix}",
                FullName = "Trần Tranh Chấp Lịch Hẹn",
                DateOfBirth = new DateOnly(1992, 7, 19),
                Gender = "FEMALE",
                Phone = "0902223344",
                Status = "ACTIVE",
            };
            Db.Patients.AddRange(patientA, patientB);
            await Db.SaveChangesAsync();
            createdSpIds.Add(sp.Id);
            createdPatientIds.AddRange([patientA.Id, patientB.Id]);

            var uniqueSecond = Math.Abs(Guid.NewGuid().GetHashCode()) % 3500;
            var slotStart = today.ToDateTime(new TimeOnly(22, 0), DateTimeKind.Unspecified).AddSeconds(uniqueSecond);
            var slot = new ExaminationSlot
            {
                ServiceId = svc.Id,
                DoctorId = 1,
                RoomId = room.Id,
                StartsAt = slotStart,
                EndsAt = slotStart.AddMinutes(30),
                Capacity = 5,
                Status = "OPEN",
            };
            Db.ExaminationSlots.Add(slot);
            await Db.SaveChangesAsync();
            createdSlotIds.Add(slot.Id);

            var appt = new Appointment
            {
                AppointmentCode = $"APT-P5-{suffix}",
                PatientId = patientB.Id,
                SlotId = slot.Id,
                BookingChannel = "COUNTER",
                ReasonForVisit = "Đặt lịch trước kiểm tra khóa đồng thời",
                Status = "BOOKED",
            };
            Db.Appointments.Add(appt);
            await Db.SaveChangesAsync();
            createdAppointmentIds.Add(appt.Id);

            var token = GenerateToken();
            using var client1 = Factory.CreateClient();
            using var client2 = Factory.CreateClient();

            // 1A. Concurrent duplicate Walk-In Visits for Patient A -> [201 Created, 409 Conflict]
            var walkInPayloadA = new CreateWalkInVisitRequest(
                PatientCode: patientA.PatientCode,
                DepartmentId: "2",
                ServiceCode: svc.ServiceCode,
                ClinicalPriority: "NORMAL",
                ReasonForVisit: "Khám tiêu hóa kiểm tra chống trùng lượt khám trong ngày",
                PaymentMethod: "CASHIER",
                IsIdentityVerified: true);

            var raceA = await Task.WhenAll(
                SendAsync(client1, HttpMethod.Post, "/api/v1/reception/visits/walk-in", walkInPayloadA, token),
                SendAsync(client2, HttpMethod.Post, "/api/v1/reception/visits/walk-in", walkInPayloadA, token));

            var codesA = raceA.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], codesA);

            // 1B. Concurrent CheckInScheduledPatient vs CreateWalkInVisit for Patient B -> Check-in 200 OK, Walk-in 409 Conflict
            var checkInPayloadB = new CheckInScheduledPatientRequest(
                ReasonForVisit: "Check-in lịch hẹn hợp lệ",
                IsIdentityVerified: true,
                Priority: "NORMAL");
            var walkInPayloadB = new CreateWalkInVisitRequest(
                PatientCode: patientB.PatientCode,
                DepartmentId: "2",
                ServiceCode: svc.ServiceCode,
                ClinicalPriority: "NORMAL",
                ReasonForVisit: "Đăng ký vãng lai song song với check-in",
                PaymentMethod: "CASHIER",
                IsIdentityVerified: true);

            var checkInTask = SendAsync(
                client1,
                HttpMethod.Post,
                $"/api/v1/reception/appointments/{appt.AppointmentCode}/check-in",
                checkInPayloadB,
                token);
            var walkInTask = SendAsync(
                client2,
                HttpMethod.Post,
                "/api/v1/reception/visits/walk-in",
                walkInPayloadB,
                token);

            await Task.WhenAll(checkInTask, walkInTask);
            var checkInRes = await checkInTask;
            var walkInRes = await walkInTask;

            Assert.Equal(HttpStatusCode.OK, checkInRes.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, walkInRes.StatusCode);

            using var verifyScope = Factory.Services.CreateScope();
            var vdb = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

            var visitsA = await vdb.Visits.AsNoTracking().Where(v => v.PatientId == patientA.Id).ToListAsync();
            var singleVisitA = Assert.Single(visitsA);
            var chargeA = await vdb.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == singleVisitA.Id);
            Assert.Equal(VisitChargeTypes.Examination, chargeA.ChargeType);
            Assert.Equal(200_000m, chargeA.UnitPrice);
            Assert.Equal(VisitChargeStatuses.Active, chargeA.Status);

            var visitsB = await vdb.Visits.AsNoTracking().Where(v => v.PatientId == patientB.Id).ToListAsync();
            var singleVisitB = Assert.Single(visitsB);
            Assert.Equal(appt.Id, singleVisitB.AppointmentId);
            var chargeB = await vdb.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == singleVisitB.Id);
            Assert.Equal(VisitChargeTypes.Examination, chargeB.ChargeType);
            Assert.Equal(200_000m, chargeB.UnitPrice);
        }
        finally
        {
            await CleanupByEntitiesAsync(
                createdPatientIds,
                createdRoomIds,
                createdSpIds,
                createdServiceIds,
                createdSlotIds,
                createdAppointmentIds);
        }
    }

    [Fact]
    public async Task ConcurrentQueueNumberAllocation_RoutedAndUnroutedWalkIns_AllocatesUniqueNumbersAndKeepsUnroutedUnbound()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        var vnNow = VietnamNow();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var createdPatientIds = new List<long>();
        var createdRoomIds = new List<long>();
        var createdSpIds = new List<long>();
        var createdServiceIds = new List<long>();

        try
        {
            var svc = new Service
            {
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = $"P5S2-{suffix}",
                Name = $"Khám Tiêu hóa P05-2 {suffix}",
                ServiceType = "EXAMINATION",
                IsActive = true,
            };
            Db.Services.Add(svc);
            await Db.SaveChangesAsync();
            createdServiceIds.Add(svc.Id);

            Db.ServicePrices.Add(new ServicePrice
            {
                ServiceId = svc.Id,
                PriceAmount = 180_000m,
                EffectiveFrom = vnNow.AddDays(-10),
            });

            var room = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = $"P5R2-{suffix}"[..12],
                Name = $"Phòng P05-2 {suffix}",
                RoomType = "EXAMINATION",
                Capacity = 6,
                Status = "ACTIVE",
            };
            Db.HospitalRooms.Add(room);
            await Db.SaveChangesAsync();
            createdRoomIds.Add(room.Id);

            var sp = new QueueServicePoint
            {
                HospitalId = 1,
                RoomId = room.Id,
                DoctorId = 1,
                ServiceId = svc.Id,
                PointType = "DOCTOR",
                Name = $"SP P05-2 {suffix}",
                IsActive = true,
            };
            Db.QueueServicePoints.Add(sp);

            var patients = Enumerable.Range(1, 4)
                .Select(i => new Patient
                {
                    PatientCode = $"BN-P5Q{i}-{suffix}",
                    FullName = $"Bệnh nhân Cấp STT {i}",
                    DateOfBirth = new DateOnly(1988, 1, i),
                    Gender = i % 2 == 0 ? "FEMALE" : "MALE",
                    Phone = $"091300000{i}",
                    Status = "ACTIVE",
                })
                .ToList();
            Db.Patients.AddRange(patients);
            await Db.SaveChangesAsync();
            createdSpIds.Add(sp.Id);
            createdPatientIds.AddRange(patients.Select(p => p.Id));

            var token = GenerateToken();
            using var c1 = Factory.CreateClient();
            using var c2 = Factory.CreateClient();

            // 2A. Concurrent routed walk-ins into the same ServicePoint
            var routed1 = new CreateWalkInVisitRequest(
                PatientCode: patients[0].PatientCode,
                DepartmentId: "2",
                ServiceCode: svc.ServiceCode,
                DoctorCode: "DOC-001",
                RoomCode: room.RoomCode,
                ReasonForVisit: "Khám vãng lai phân buồng 1",
                PaymentMethod: "CASHIER",
                IsIdentityVerified: true);
            var routed2 = routed1 with { PatientCode = patients[1].PatientCode, ReasonForVisit = "Khám vãng lai phân buồng 2" };

            var routedRes = await Task.WhenAll(
                SendAsync(c1, HttpMethod.Post, "/api/v1/reception/visits/walk-in", routed1, token),
                SendAsync(c2, HttpMethod.Post, "/api/v1/reception/visits/walk-in", routed2, token));

            Assert.All(routedRes, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            var routedDtos = await Task.WhenAll(routedRes.Select(r => r.Content.ReadFromJsonAsync<CreateWalkInVisitResponseDto>()));
            var routedNumbers = routedDtos.Select(d => d!.QueueNumber).OrderBy(n => n).ToArray();
            Assert.Equal([1, 2], routedNumbers);

            // 2B. Concurrent unrouted walk-ins (no room/doctor) -> unique numbers under hospital advisory lock & unbound SP
            var unrouted1 = new CreateWalkInVisitRequest(
                PatientCode: patients[2].PatientCode,
                DepartmentId: "2",
                ServiceCode: svc.ServiceCode,
                ReasonForVisit: "Khám vãng lai chưa phân buồng 1",
                PaymentMethod: "CASHIER",
                IsIdentityVerified: true);
            var unrouted2 = unrouted1 with { PatientCode = patients[3].PatientCode, ReasonForVisit = "Khám vãng lai chưa phân buồng 2" };

            var unroutedRes = await Task.WhenAll(
                SendAsync(c1, HttpMethod.Post, "/api/v1/reception/visits/walk-in", unrouted1, token),
                SendAsync(c2, HttpMethod.Post, "/api/v1/reception/visits/walk-in", unrouted2, token));

            Assert.All(unroutedRes, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            var unroutedDtos = await Task.WhenAll(unroutedRes.Select(r => r.Content.ReadFromJsonAsync<CreateWalkInVisitResponseDto>()));
            Assert.NotEqual(unroutedDtos[0]!.QueueNumber, unroutedDtos[1]!.QueueNumber);

            using var verifyScope = Factory.Services.CreateScope();
            var vdb = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var unroutedVisitIds = unroutedDtos.Select(d => d!.VisitId).ToArray();
            var unroutedQueues = await vdb.QueueEntries.AsNoTracking()
                .Where(q => q.VisitId != null && unroutedVisitIds.Contains(q.VisitId.Value))
                .ToListAsync();
            Assert.Equal(2, unroutedQueues.Count);
            Assert.All(unroutedQueues, q =>
            {
                Assert.Null(q.ServicePointId);
                Assert.Null(q.CurrentServicePointId);
            });
        }
        finally
        {
            await CleanupByEntitiesAsync(
                createdPatientIds,
                createdRoomIds,
                createdSpIds,
                createdServiceIds,
                [],
                []);
        }
    }

    [Fact]
    public async Task ConcurrentCallNextAndExaminationPaymentGate_SkipsUnpaidEnforcesCapacityAndRejectsWhenOnlyUnpaidRemain()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        var vnNow = VietnamNow();
        var today = DateOnly.FromDateTime(vnNow);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var createdPatientIds = new List<long>();
        var createdRoomIds = new List<long>();
        var createdSpIds = new List<long>();

        try
        {
            var room = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = $"P5R3-{suffix}"[..12],
                Name = $"Phòng P05-3 {suffix}",
                RoomType = "EXAMINATION",
                Capacity = 1,
                Status = "ACTIVE",
            };
            Db.HospitalRooms.Add(room);
            await Db.SaveChangesAsync();
            createdRoomIds.Add(room.Id);

            var sp = new QueueServicePoint
            {
                HospitalId = 1,
                RoomId = room.Id,
                DoctorId = 1,
                PointType = "DOCTOR",
                Name = $"SP P05-3 {suffix}",
                IsActive = true,
            };
            Db.QueueServicePoints.Add(sp);

            var pUnpaid = new Patient
            {
                PatientCode = $"BN-P5U-{suffix}",
                FullName = "Bệnh nhân Chưa Đóng Phí Khám",
                DateOfBirth = new DateOnly(1990, 2, 2),
                Gender = "MALE",
                Status = "ACTIVE",
            };
            var pPaid = new Patient
            {
                PatientCode = $"BN-P5P-{suffix}",
                FullName = "Bệnh nhân Đã Đóng Phí Khám",
                DateOfBirth = new DateOnly(1991, 3, 3),
                Gender = "FEMALE",
                Status = "ACTIVE",
            };
            Db.Patients.AddRange(pUnpaid, pPaid);
            await Db.SaveChangesAsync();
            createdSpIds.Add(sp.Id);
            createdPatientIds.AddRange([pUnpaid.Id, pPaid.Id]);

            var visitUnpaid = new Visit
            {
                VisitCode = $"VIS-P5U-{suffix}",
                PatientId = pUnpaid.Id,
                AssignedDoctorId = 1,
                ExaminationRoomId = room.Id,
                CheckedInByStaffId = 3,
                Status = "WAITING",
                CheckedInAt = vnNow.AddMinutes(-20),
            };
            var visitPaid = new Visit
            {
                VisitCode = $"VIS-P5P-{suffix}",
                PatientId = pPaid.Id,
                AssignedDoctorId = 1,
                ExaminationRoomId = room.Id,
                CheckedInByStaffId = 3,
                Status = "WAITING",
                CheckedInAt = vnNow.AddMinutes(-10),
            };
            Db.Visits.AddRange(visitUnpaid, visitPaid);
            await Db.SaveChangesAsync();

            var chargeUnpaid = VisitCharge.CreateExamination(visitUnpaid.Id, serviceId: 1, unitPrice: 250_000m, createdAtLocal: vnNow, waived: false);
            var chargePaid = VisitCharge.CreateExamination(visitPaid.Id, serviceId: 1, unitPrice: 250_000m, createdAtLocal: vnNow, waived: false);
            Db.VisitCharges.AddRange(chargeUnpaid, chargePaid);
            await Db.SaveChangesAsync();

            var paymentPaid = Payment.CreateForCharge(visitPaid.Id, chargePaid.Id, 250_000m, vnNow);
            Db.Payments.Add(paymentPaid);
            await Db.SaveChangesAsync();
            paymentPaid.PaymentCode = $"MF{paymentPaid.Id}";
            paymentPaid.Status = "PAID";
            paymentPaid.PaidAt = vnNow;
            await Db.SaveChangesAsync();

            var queueUnpaid = new QueueEntry
            {
                VisitId = visitUnpaid.Id,
                ServicePointId = sp.Id,
                CurrentServicePointId = sp.Id,
                QueueDate = today,
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "ELDERLY",
                StateVersion = 1,
                ArrivedAt = vnNow.AddMinutes(-20),
                ReadyAt = vnNow.AddMinutes(-20),
            };
            var queuePaid = new QueueEntry
            {
                VisitId = visitPaid.Id,
                ServicePointId = sp.Id,
                CurrentServicePointId = sp.Id,
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
            Db.QueueEntries.AddRange(queueUnpaid, queuePaid);
            await Db.SaveChangesAsync();

            var token = GenerateToken();
            using var c1 = Factory.CreateClient();
            using var c2 = Factory.CreateClient();

            // Concurrent CallNext on Capacity=1 room -> [200 OK (skips unpaid #1, calls paid #2), 409 Conflict]
            var callRes = await Task.WhenAll(
                SendAsync(c1, HttpMethod.Post, $"/api/v1/queue/service-points/sp-{sp.Id}/call-next", new CallNextPatientRequest(Notes: "Race 1"), token),
                SendAsync(c2, HttpMethod.Post, $"/api/v1/queue/service-points/sp-{sp.Id}/call-next", new CallNextPatientRequest(Notes: "Race 2"), token));

            var callCodes = callRes.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], callCodes);

            var winnerRes = callRes.Single(r => r.StatusCode == HttpStatusCode.OK);
            var winnerDto = await winnerRes.Content.ReadFromJsonAsync<CallNextPatientResponseDto>();
            Assert.NotNull(winnerDto);
            Assert.Equal("CALLED", winnerDto.Outcome);
            Assert.Equal(visitPaid.VisitCode, winnerDto.CalledEntry!.VisitCode);

            // Board exposes UNPAID_EXAMINATION_FEE on Candidate #1
            using var boardRes = await SendAsync(
                c1,
                HttpMethod.Get,
                $"/api/v1/reception/examination-queue?servicePointId=sp-{sp.Id}",
                null,
                token);
            Assert.Equal(HttpStatusCode.OK, boardRes.StatusCode);
            var board = await boardRes.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
            var spEntries = Assert.Single(board!.ServicePoints).Entries;
            var unpaidBoardEntry = Assert.Single(spEntries, e => e.VisitCode == visitUnpaid.VisitCode);
            Assert.Equal("UNPAID_EXAMINATION_FEE", unpaidBoardEntry.BlockedReason);

            // Mark paid candidate COMPLETED so room is idle; now ONLY unpaid Candidate #1 remains -> CallNext returns 409 Conflict
            using (var scope = Factory.Services.CreateScope())
            {
                var sdb = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var qPaidDb = await sdb.QueueEntries.SingleAsync(q => q.Id == queuePaid.Id);
                qPaidDb.Status = "COMPLETED";
                qPaidDb.CompletedAt = vnNow;
                var vPaidDb = await sdb.Visits.SingleAsync(v => v.Id == visitPaid.Id);
                vPaidDb.Status = "COMPLETED";
                vPaidDb.CompletedAt = vnNow;
                await sdb.SaveChangesAsync();
            }

            using var onlyUnpaidRes = await SendAsync(
                c1,
                HttpMethod.Post,
                $"/api/v1/queue/service-points/sp-{sp.Id}/call-next",
                new CallNextPatientRequest(Notes: "Only unpaid waiting"),
                token);
            Assert.Equal(HttpStatusCode.Conflict, onlyUnpaidRes.StatusCode);
        }
        finally
        {
            await CleanupByEntitiesAsync(
                createdPatientIds,
                createdRoomIds,
                createdSpIds,
                [],
                [],
                []);
        }
    }

    [Fact]
    public async Task ConcurrentRoomTransferVsSourceCallNext_ZeroDeadlocksPreservesOriginTicketNoAndResetsCalled()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        var vnNow = VietnamNow();
        var today = DateOnly.FromDateTime(vnNow);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var createdPatientIds = new List<long>();
        var createdRoomIds = new List<long>();
        var createdSpIds = new List<long>();

        try
        {
            var roomA = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = $"P5RA-{suffix}"[..12],
                Name = $"Phòng Nguồn A {suffix}",
                RoomType = "EXAMINATION",
                Capacity = 1,
                Status = "ACTIVE",
            };
            var roomB = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = $"P5RB-{suffix}"[..12],
                Name = $"Phòng Đích B {suffix}",
                RoomType = "EXAMINATION",
                Capacity = 2,
                Status = "ACTIVE",
            };
            Db.HospitalRooms.AddRange(roomA, roomB);
            await Db.SaveChangesAsync();
            createdRoomIds.AddRange([roomA.Id, roomB.Id]);

            var spA = new QueueServicePoint
            {
                HospitalId = 1,
                RoomId = roomA.Id,
                DoctorId = 1,
                PointType = "DOCTOR",
                Name = $"SP Nguồn A {suffix}",
                IsActive = true,
            };
            var spB = new QueueServicePoint
            {
                HospitalId = 1,
                RoomId = roomB.Id,
                DoctorId = 1,
                PointType = "DOCTOR",
                Name = $"SP Đích B {suffix}",
                IsActive = true,
            };
            Db.QueueServicePoints.AddRange(spA, spB);

            var p1 = new Patient { PatientCode = $"BN-P5T1-{suffix}", FullName = "Bệnh nhân Chuyển Phòng", Status = "ACTIVE" };
            var p2 = new Patient { PatientCode = $"BN-P5T2-{suffix}", FullName = "Bệnh nhân Tiếp Theo Phòng A", Status = "ACTIVE" };
            Db.Patients.AddRange(p1, p2);
            await Db.SaveChangesAsync();
            createdSpIds.AddRange([spA.Id, spB.Id]);
            createdPatientIds.AddRange([p1.Id, p2.Id]);

            var visit1 = new Visit
            {
                VisitCode = $"VIS-P5T1-{suffix}",
                PatientId = p1.Id,
                AssignedDoctorId = 1,
                ExaminationRoomId = roomA.Id,
                CheckedInByStaffId = 3,
                Status = "WAITING",
                CheckedInAt = vnNow.AddMinutes(-15),
            };
            var visit2 = new Visit
            {
                VisitCode = $"VIS-P5T2-{suffix}",
                PatientId = p2.Id,
                AssignedDoctorId = 1,
                ExaminationRoomId = roomA.Id,
                CheckedInByStaffId = 3,
                Status = "WAITING",
                CheckedInAt = vnNow.AddMinutes(-10),
            };
            Db.Visits.AddRange(visit1, visit2);
            await Db.SaveChangesAsync();

            Db.VisitCharges.AddRange(
                VisitCharge.CreateExamination(visit1.Id, serviceId: 1, unitPrice: 0m, createdAtLocal: vnNow, waived: true),
                VisitCharge.CreateExamination(visit2.Id, serviceId: 1, unitPrice: 0m, createdAtLocal: vnNow, waived: true));

            var queue1 = new QueueEntry
            {
                VisitId = visit1.Id,
                ServicePointId = spA.Id,
                CurrentServicePointId = spA.Id,
                QueueDate = today,
                QueueNumber = 7,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = vnNow.AddMinutes(-15),
                ReadyAt = vnNow.AddMinutes(-15),
            };
            var queue2 = new QueueEntry
            {
                VisitId = visit2.Id,
                ServicePointId = spA.Id,
                CurrentServicePointId = spA.Id,
                QueueDate = today,
                QueueNumber = 8,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = vnNow.AddMinutes(-10),
                ReadyAt = vnNow.AddMinutes(-10),
            };
            Db.QueueEntries.AddRange(queue1, queue2);
            await Db.SaveChangesAsync();

            var token = GenerateToken();
            using var c1 = Factory.CreateClient();
            using var c2 = Factory.CreateClient();

            // Capture original TicketNo in Room A before transfer
            using var preBoardRes = await SendAsync(
                c1,
                HttpMethod.Get,
                $"/api/v1/reception/examination-queue?servicePointId=sp-{spA.Id}",
                null,
                token);
            var preBoard = await preBoardRes.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
            var originalTicketNo = Assert.Single(
                Assert.Single(preBoard!.ServicePoints).Entries,
                e => e.VisitCode == visit1.VisitCode).TicketNo;

            // Fire Transfer (Room A -> Room B) concurrently with CallNext on Room A
            var transferTask = SendAsync(
                c1,
                HttpMethod.Post,
                $"/api/v1/visits/{visit1.VisitCode}/transfer-room",
                new TransferVisitRoomRequest(
                    TargetRoomId: roomB.Id,
                    Reason: "Điều phối giảm tải sang phòng B đồng thời với CallNext",
                    TargetDoctorId: 1),
                token);
            var callNextTask = SendAsync(
                c2,
                HttpMethod.Post,
                $"/api/v1/queue/service-points/sp-{spA.Id}/call-next",
                new CallNextPatientRequest(Notes: "CallNext Room A concurrent with transfer"),
                token);

            await Task.WhenAll(transferTask, callNextTask);
            var transferRes = await transferTask;
            var callNextRes = await callNextTask;

            Assert.Equal(HttpStatusCode.OK, transferRes.StatusCode);
            Assert.Equal(HttpStatusCode.OK, callNextRes.StatusCode);

            using var verifyScope = Factory.Services.CreateScope();
            var vdb = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

            var v1After = await vdb.Visits.AsNoTracking().SingleAsync(v => v.Id == visit1.Id);
            var q1After = await vdb.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == queue1.Id);

            // Regardless of serialization order, Visit 1 MUST end up WAITING in Room B (never stuck CALLED in Room A!)
            Assert.Equal(roomB.Id, v1After.ExaminationRoomId);
            Assert.Equal("WAITING", v1After.Status);
            Assert.Equal(spA.Id, q1After.ServicePointId);
            Assert.Equal(spB.Id, q1After.CurrentServicePointId);
            Assert.Equal("WAITING", q1After.Status);
            Assert.Null(q1After.CalledAt);
            Assert.Equal(7, q1After.QueueNumber);

            // Verify TicketNo prefix is preserved in Room B's queue board
            using var postBoardRes = await SendAsync(
                c1,
                HttpMethod.Get,
                $"/api/v1/reception/examination-queue?servicePointId=sp-{spB.Id}",
                null,
                token);
            var postBoard = await postBoardRes.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
            var migratedEntry = Assert.Single(
                Assert.Single(postBoard!.ServicePoints).Entries,
                e => e.VisitCode == visit1.VisitCode);
            Assert.Equal(originalTicketNo, migratedEntry.TicketNo);
        }
        finally
        {
            await CleanupByEntitiesAsync(
                createdPatientIds,
                createdRoomIds,
                createdSpIds,
                [],
                [],
                []);
        }
    }

    [Fact]
    public async Task PreConsultationCancellationReconciliation_UnpaidLateWebhook_PaidRefundPending_AndReassignRequeue()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        var vnNow = VietnamNow();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var createdPatientIds = new List<long>();
        var createdRoomIds = new List<long>();
        var createdSpIds = new List<long>();
        var createdServiceIds = new List<long>();
        var createdGatewayTxIds = new List<long>();

        try
        {
            var svc = new Service
            {
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = $"P5S5-{suffix}",
                Name = $"Khám Tiêu hóa P05-5 {suffix}",
                ServiceType = "EXAMINATION",
                IsActive = true,
            };
            Db.Services.Add(svc);
            await Db.SaveChangesAsync();
            createdServiceIds.Add(svc.Id);

            Db.ServicePrices.Add(new ServicePrice
            {
                ServiceId = svc.Id,
                PriceAmount = 250_000m,
                EffectiveFrom = vnNow.AddDays(-10),
            });

            var roomA = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = $"P5CA-{suffix}"[..12],
                Name = $"Phòng Hủy A {suffix}",
                RoomType = "EXAMINATION",
                Capacity = 4,
                Status = "ACTIVE",
            };
            var roomB = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = $"P5CB-{suffix}"[..12],
                Name = $"Phòng Điều Phối B {suffix}",
                RoomType = "EXAMINATION",
                Capacity = 4,
                Status = "ACTIVE",
            };
            Db.HospitalRooms.AddRange(roomA, roomB);
            await Db.SaveChangesAsync();
            createdRoomIds.AddRange([roomA.Id, roomB.Id]);

            var spA = new QueueServicePoint { HospitalId = 1, RoomId = roomA.Id, DoctorId = 1, ServiceId = svc.Id, PointType = "DOCTOR", Name = $"SP Hủy A {suffix}", IsActive = true };
            var spB = new QueueServicePoint { HospitalId = 1, RoomId = roomB.Id, DoctorId = 1, ServiceId = svc.Id, PointType = "DOCTOR", Name = $"SP Điều Phối B {suffix}", IsActive = true };
            Db.QueueServicePoints.AddRange(spA, spB);

            var pA = new Patient { PatientCode = $"BN-P5CA-{suffix}", FullName = "BN Rút Lui Khi Chưa Thanh Toán", DateOfBirth = new DateOnly(1990, 5, 5), Gender = "MALE", Status = "ACTIVE" };
            var pB = new Patient { PatientCode = $"BN-P5CB-{suffix}", FullName = "BN Rút Lui Sau Thanh Toán", DateOfBirth = new DateOnly(1991, 6, 6), Gender = "FEMALE", Status = "ACTIVE" };
            var pC = new Patient { PatientCode = $"BN-P5CC-{suffix}", FullName = "BN Điều Phối Sang Phòng Mới", DateOfBirth = new DateOnly(1992, 7, 7), Gender = "MALE", Status = "ACTIVE" };
            Db.Patients.AddRange(pA, pB, pC);
            await Db.SaveChangesAsync();
            createdSpIds.AddRange([spA.Id, spB.Id]);
            createdPatientIds.AddRange([pA.Id, pB.Id, pC.Id]);

            var token = GenerateToken();
            using var client = Factory.CreateClient();

            // --- Sub-case A: Unpaid VIETQR walk-in -> PATIENT_ABANDONED -> late SePay webhook => CANCELLED_REFUND_PENDING ---
            using var createResA = await SendAsync(
                client,
                HttpMethod.Post,
                "/api/v1/reception/visits/walk-in",
                new CreateWalkInVisitRequest(
                    PatientCode: pA.PatientCode,
                    DepartmentId: "2",
                    ServiceCode: svc.ServiceCode,
                    DoctorCode: "DOC-001",
                    RoomCode: roomA.RoomCode,
                    ReasonForVisit: "Khám tiêu hóa thanh toán VietQR rồi xin về",
                    PaymentMethod: "VIETQR",
                    IsIdentityVerified: true),
                token);
            Assert.Equal(HttpStatusCode.Created, createResA.StatusCode);
            var walkInA = (await createResA.Content.ReadFromJsonAsync<CreateWalkInVisitResponseDto>())!;

            string paymentCodeA;
            using (var scope = Factory.Services.CreateScope())
            {
                var sdb = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var paymentA = await sdb.Payments.AsNoTracking().SingleAsync(p => p.VisitId == walkInA.VisitId);
                Assert.Equal(PaymentScopes.Charge, paymentA.PaymentScope);
                Assert.Equal("PENDING", paymentA.Status);
                paymentCodeA = paymentA.PaymentCode!;
            }

            using var cancelResA = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/reception/examination-queue/entries/{walkInA.QueueEntryId}/cancel",
                new CancelQueueEntryRequest(
                    Reason: "Bệnh nhân xin về trước khi chuyển khoản",
                    ExpectedStateVersion: 1,
                    Disposition: QueueCancelDispositions.PatientAbandoned),
                token);
            Assert.Equal(HttpStatusCode.OK, cancelResA.StatusCode);

            using (var scope = Factory.Services.CreateScope())
            {
                var sdb = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var visitDbA = await sdb.Visits.AsNoTracking().SingleAsync(v => v.Id == walkInA.VisitId);
                var chargeDbA = await sdb.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == walkInA.VisitId);
                var paymentDbA = await sdb.Payments.AsNoTracking().SingleAsync(p => p.VisitId == walkInA.VisitId);

                Assert.Equal("CANCELLED", visitDbA.Status);
                Assert.Null(visitDbA.CompletedAt);
                Assert.Equal(VisitChargeStatuses.Cancelled, chargeDbA.Status);
                Assert.Equal("EXPIRED", paymentDbA.Status);
            }

            // Late SePay bank transfer arrives AFTER cancellation
            var gatewayTxA = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(10_000, 90_000);
            createdGatewayTxIds.Add(gatewayTxA);
            using (var lateWebhookRes = await PostSePayWebhookAsync(gatewayTxA, 250_000m, $"Chuyen khoan {paymentCodeA}"))
            {
                Assert.Equal(HttpStatusCode.OK, lateWebhookRes.StatusCode);
            }

            using (var scope = Factory.Services.CreateScope())
            {
                var sdb = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var visitDbA = await sdb.Visits.AsNoTracking().SingleAsync(v => v.Id == walkInA.VisitId);
                var chargeDbA = await sdb.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == walkInA.VisitId);
                var paymentDbA = await sdb.Payments.AsNoTracking().SingleAsync(p => p.VisitId == walkInA.VisitId);

                Assert.Equal("CANCELLED", visitDbA.Status);
                Assert.Null(visitDbA.CompletedAt);
                Assert.Equal(VisitChargeStatuses.CancelledRefundPending, chargeDbA.Status);
                Assert.Equal("MISMATCH", paymentDbA.Status);
            }

            // --- Sub-case B: Paid VIETQR walk-in -> PATIENT_ABANDONED => PAID Payment immutable + CANCELLED_REFUND_PENDING ---
            using var createResB = await SendAsync(
                client,
                HttpMethod.Post,
                "/api/v1/reception/visits/walk-in",
                new CreateWalkInVisitRequest(
                    PatientCode: pB.PatientCode,
                    DepartmentId: "2",
                    ServiceCode: svc.ServiceCode,
                    DoctorCode: "DOC-001",
                    RoomCode: roomA.RoomCode,
                    ReasonForVisit: "Khám tiêu hóa đã thanh toán VietQR nhưng xin về trước khám",
                    PaymentMethod: "VIETQR",
                    IsIdentityVerified: true),
                token);
            Assert.Equal(HttpStatusCode.Created, createResB.StatusCode);
            var walkInB = (await createResB.Content.ReadFromJsonAsync<CreateWalkInVisitResponseDto>())!;

            string paymentCodeB;
            using (var scope = Factory.Services.CreateScope())
            {
                var sdb = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var paymentB = await sdb.Payments.AsNoTracking().SingleAsync(p => p.VisitId == walkInB.VisitId);
                paymentCodeB = paymentB.PaymentCode!;
            }

            var gatewayTxB = gatewayTxA + 1;
            createdGatewayTxIds.Add(gatewayTxB);
            using (var paidWebhookRes = await PostSePayWebhookAsync(gatewayTxB, 250_000m, $"Chuyen khoan {paymentCodeB}"))
            {
                Assert.Equal(HttpStatusCode.OK, paidWebhookRes.StatusCode);
            }

            using var cancelResB = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/reception/examination-queue/entries/{walkInB.QueueEntryId}/cancel",
                new CancelQueueEntryRequest(
                    Reason: "Bệnh nhân có việc đột xuất xin về sau khi đã đóng phí",
                    ExpectedStateVersion: 1,
                    Disposition: QueueCancelDispositions.PatientAbandoned),
                token);
            Assert.Equal(HttpStatusCode.OK, cancelResB.StatusCode);

            using (var scope = Factory.Services.CreateScope())
            {
                var sdb = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var visitDbB = await sdb.Visits.AsNoTracking().SingleAsync(v => v.Id == walkInB.VisitId);
                var chargeDbB = await sdb.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == walkInB.VisitId);
                var paymentDbB = await sdb.Payments.AsNoTracking().SingleAsync(p => p.VisitId == walkInB.VisitId);

                Assert.Equal("CANCELLED", visitDbB.Status);
                Assert.Null(visitDbB.CompletedAt);
                Assert.Equal(VisitChargeStatuses.CancelledRefundPending, chargeDbB.Status);
                Assert.Equal("PAID", paymentDbB.Status);

                var refundAuditExists = await sdb.AuditLogs.AsNoTracking().AnyAsync(a =>
                    a.Action == QueueStatusService.ExaminationChargeRefundPendingAction &&
                    a.EntityId == chargeDbB.Id.ToString(CultureInfo.InvariantCulture));
                Assert.True(refundAuditExists);
            }

            // --- Sub-case C: REASSIGN_REQUEUE cancels old QueueEntry while keeping Visit & VisitCharge ACTIVE in TargetRoom ---
            using var createResC = await SendAsync(
                client,
                HttpMethod.Post,
                "/api/v1/reception/visits/walk-in",
                new CreateWalkInVisitRequest(
                    PatientCode: pC.PatientCode,
                    DepartmentId: "2",
                    ServiceCode: svc.ServiceCode,
                    DoctorCode: "DOC-001",
                    RoomCode: roomA.RoomCode,
                    ReasonForVisit: "Khám tiêu hóa điều phối sang phòng B",
                    PaymentMethod: "CASHIER",
                    IsIdentityVerified: true),
                token);
            Assert.Equal(HttpStatusCode.Created, createResC.StatusCode);
            var walkInC = (await createResC.Content.ReadFromJsonAsync<CreateWalkInVisitResponseDto>())!;

            using var cancelResC = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/reception/examination-queue/entries/{walkInC.QueueEntryId}/cancel",
                new CancelQueueEntryRequest(
                    Reason: "Hủy dòng hàng đợi phòng A để xếp lại sang phòng B",
                    ExpectedStateVersion: 1,
                    Disposition: QueueCancelDispositions.ReassignRequeue,
                    TargetRoomId: roomB.Id,
                    TargetDoctorId: 1),
                token);
            Assert.Equal(HttpStatusCode.OK, cancelResC.StatusCode);

            using (var scope = Factory.Services.CreateScope())
            {
                var sdb = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var visitDbC = await sdb.Visits.AsNoTracking().SingleAsync(v => v.Id == walkInC.VisitId);
                var chargeDbC = await sdb.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == walkInC.VisitId);
                var queuesC = await sdb.QueueEntries.AsNoTracking().Where(q => q.VisitId == walkInC.VisitId).ToListAsync();

                Assert.Equal("WAITING", visitDbC.Status);
                Assert.Equal(roomB.Id, visitDbC.ExaminationRoomId);
                Assert.Equal(VisitChargeStatuses.Active, chargeDbC.Status);
                Assert.Contains(queuesC, q => q.Id == walkInC.QueueEntryId && q.Status == "CANCELLED");
                Assert.Contains(queuesC, q => q.Id != walkInC.QueueEntryId && q.Status == "WAITING" && q.CurrentServicePointId == spB.Id);
            }
        }
        finally
        {
            await CleanupByEntitiesAsync(
                createdPatientIds,
                createdRoomIds,
                createdSpIds,
                createdServiceIds,
                [],
                []);
        }
    }

    private string GenerateToken(long userId = 5, string email = "reception01@medflow.test", params string[] roles)
    {
        var effectiveRoles = roles.Length > 0 ? roles : ["RECEPTION"];
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(new User { Id = userId, Email = email }, effectiveRoles).AccessToken;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        object? body,
        string token)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(req);
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

        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Apikey", SePayApiKey);
        return await Client.SendAsync(request);
    }

    private async Task CleanupByEntitiesAsync(
        IReadOnlyCollection<long> patientIds,
        IReadOnlyCollection<long> roomIds,
        IReadOnlyCollection<long> spIds,
        IReadOnlyCollection<long> serviceIds,
        IReadOnlyCollection<long> slotIds,
        IReadOnlyCollection<long> appointmentIds)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var visitIds = patientIds.Count == 0
            ? []
            : await db.Visits.AsNoTracking()
                .Where(v => v.PatientId != null && patientIds.Contains(v.PatientId.Value))
                .Select(v => v.Id)
                .ToListAsync();

        var paymentIds = visitIds.Count == 0
            ? []
            : await db.Payments.AsNoTracking()
                .Where(p => p.VisitId != null && visitIds.Contains(p.VisitId.Value))
                .Select(p => p.Id)
                .ToListAsync();

        var chargeIds = visitIds.Count == 0
            ? []
            : await db.VisitCharges.AsNoTracking()
                .Where(c => c.VisitId != null && visitIds.Contains(c.VisitId.Value))
                .Select(c => c.Id)
                .ToListAsync();

        var queueIds = visitIds.Count == 0 && appointmentIds.Count == 0 && spIds.Count == 0
            ? []
            : await db.QueueEntries.AsNoTracking()
                .Where(q =>
                    (q.VisitId != null && visitIds.Contains(q.VisitId.Value)) ||
                    (q.AppointmentId != null && appointmentIds.Contains(q.AppointmentId.Value)) ||
                    (q.ServicePointId != null && spIds.Contains(q.ServicePointId.Value)) ||
                    (q.CurrentServicePointId != null && spIds.Contains(q.CurrentServicePointId.Value)))
                .Select(q => q.Id)
                .ToListAsync();

        if (paymentIds.Count > 0)
        {
            await db.PaymentGatewayEvents
                .Where(e => e.PaymentId != null && paymentIds.Contains(e.PaymentId.Value))
                .ExecuteDeleteAsync();
            await db.Payments
                .Where(p => paymentIds.Contains(p.Id))
                .ExecuteDeleteAsync();
        }

        if (chargeIds.Count > 0)
        {
            await db.VisitCharges
                .Where(c => chargeIds.Contains(c.Id))
                .ExecuteDeleteAsync();
        }

        if (appointmentIds.Count > 0)
        {
            await db.Notifications
                .Where(n => n.AppointmentId != null && appointmentIds.Contains(n.AppointmentId.Value))
                .ExecuteDeleteAsync();
        }

        if (spIds.Count > 0)
        {
            await db.QueueFairnessStates
                .Where(s => spIds.Contains(s.ServicePointId))
                .ExecuteDeleteAsync();
        }

        if (queueIds.Count > 0)
        {
            await db.QueueEntries
                .Where(q => queueIds.Contains(q.Id))
                .ExecuteDeleteAsync();
        }

        var entityIdStrings = visitIds
            .Concat(queueIds)
            .Concat(appointmentIds)
            .Concat(paymentIds)
            .Concat(chargeIds)
            .Distinct()
            .Select(id => id.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        if (entityIdStrings.Length > 0)
        {
            await db.AuditLogs
                .Where(a => a.EntityId != null && entityIdStrings.Contains(a.EntityId))
                .ExecuteDeleteAsync();
        }

        if (visitIds.Count > 0)
        {
            await db.Visits
                .Where(v => visitIds.Contains(v.Id))
                .ExecuteDeleteAsync();
        }

        if (appointmentIds.Count > 0)
        {
            await db.Appointments
                .Where(a => appointmentIds.Contains(a.Id))
                .ExecuteDeleteAsync();
        }

        if (slotIds.Count > 0)
        {
            await db.ExaminationSlots
                .Where(s => slotIds.Contains(s.Id))
                .ExecuteDeleteAsync();
        }

        if (spIds.Count > 0)
        {
            await db.QueueServicePoints
                .Where(sp => spIds.Contains(sp.Id))
                .ExecuteDeleteAsync();
        }

        if (roomIds.Count > 0)
        {
            await db.HospitalRooms
                .Where(r => roomIds.Contains(r.Id))
                .ExecuteDeleteAsync();
        }

        if (serviceIds.Count > 0)
        {
            await db.ServicePrices
                .Where(p => p.ServiceId != null && serviceIds.Contains(p.ServiceId.Value))
                .ExecuteDeleteAsync();
            await db.Services
                .Where(s => serviceIds.Contains(s.Id))
                .ExecuteDeleteAsync();
        }

        if (patientIds.Count > 0)
        {
            await db.Patients
                .Where(p => patientIds.Contains(p.Id))
                .ExecuteDeleteAsync();
        }
    }

    private static DateTime VietnamNow() =>
        DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
}
