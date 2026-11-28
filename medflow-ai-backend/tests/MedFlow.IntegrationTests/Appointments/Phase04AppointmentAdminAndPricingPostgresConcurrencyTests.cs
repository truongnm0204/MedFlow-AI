using System.Globalization;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.ExaminationRooms;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Reception;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.Infrastructure.BackgroundJobs;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedFlow.IntegrationTests.Appointments;

/// <summary>
/// Phase 04 (GH-844 / ADR-0059) Mandatory Real-PostgreSQL Concurrency &amp; State-Machine Suite.
/// Executes against <c>medflow_test_p04</c> with independent DI scopes and real PostgreSQL FOR UPDATE row locks.
/// Covers BUG-P4-01 .. BUG-P4-07:
/// 1) All 5 appointment race pairs (Check-in vs Cancel, Check-in vs Reschedule, Cancel vs Reschedule,
///    Reschedule vs Reschedule under lineage MaxReschedules, NO_SHOW cronjob vs Check-in).
/// 2) Service-mode parallel room slot booking under contention &amp; Doctor-mode strictness.
/// 3) Cronjob reminder starvation past first batch + FAILED outbox non-duplication.
/// 4) Concurrent Booking vs Admin Deactivation + Active QueueEntry blocks on PUT &amp; /deactivate.
/// 5) Concurrent Admin A vs Admin B ServicePrice updates, half-open promo split/resume &amp; VisitCharge/Payment immutability.
/// </summary>
public sealed class Phase04AppointmentAdminAndPricingPostgresConcurrencyTests
    : IClassFixture<TestWebAppFactory>, IDisposable
{
    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private long _utcTicks = initialUtc.UtcTicks;

        public override DateTimeOffset GetUtcNow()
            => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void SetVietnamLocalTime(DateTime vnLocalUnspecified)
        {
            var utc = DateTime.SpecifyKind(vnLocalUnspecified.AddHours(-7), DateTimeKind.Utc);
            Interlocked.Exchange(ref _utcTicks, new DateTimeOffset(utc).UtcTicks);
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed record IsolatedPhase04Fixture(
        string Tag,
        long HospitalId,
        long DepartmentId,
        long SpecialtyId,
        long Room1Id,
        long Room2Id,
        long Doctor1Id,
        string Doctor1Code,
        long Doctor2Id,
        string Doctor2Code,
        long ExamServiceId,
        string ExamServiceCode,
        long DiagServiceId,
        long ExamServicePointId,
        long DiagServicePointId,
        long Admin1UserId,
        long Admin2UserId,
        long ReceptionistUserId,
        List<long> CreatedUserIds,
        List<long> CreatedPatientIds);

    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 11, 26, 2, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> _app;

    public Phase04AppointmentAdminAndPricingPostgresConcurrencyTests(TestWebAppFactory baseFactory)
    {
        _app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
            });
        });
    }

    public void Dispose()
    {
        _app.Dispose();
    }

    private async Task<IsolatedPhase04Fixture> CreateIsolatedFixtureAsync(int maxReschedules = 2)
    {
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_clock);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var adminRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.HospitalAdmin);
        var recepRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Reception);
        var docRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Doctor);

        var hospital = new Hospital
        {
            HospitalCode = $"P04-{tag}",
            Name = $"Bệnh viện Test Phase 04 {tag}",
            IsActive = true,
        };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();

        var department = new Department
        {
            HospitalId = hospital.Id,
            DepartmentCode = $"DP-{tag}",
            Name = $"Khoa Nội {tag}",
            IsActive = true,
        };
        var specialty = new Specialty
        {
            SpecialtyCode = $"SP-{tag}",
            Name = $"Chuyên khoa Nội {tag}",
            IsActive = true,
        };
        db.Departments.Add(department);
        db.Specialties.Add(specialty);
        await db.SaveChangesAsync();

        var room1 = new HospitalRoom
        {
            HospitalId = hospital.Id,
            DepartmentId = department.Id,
            RoomCode = $"R1-{tag}",
            Name = $"Buồng khám 1 {tag}",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        };
        var room2 = new HospitalRoom
        {
            HospitalId = hospital.Id,
            DepartmentId = department.Id,
            RoomCode = $"R2-{tag}",
            Name = $"Buồng khám 2 {tag}",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        };
        db.HospitalRooms.AddRange(room1, room2);
        await db.SaveChangesAsync();

        var createdUserIds = new List<long>();

        async Task<(User User, Staff Staff)> CreateStaffAsync(string prefix, long roleId)
        {
            var u = new User
            {
                Email = $"p04-{prefix}-{tag.ToLowerInvariant()}-{Guid.NewGuid():N}@medflow.test",
                AuthProvider = "LOCAL",
                AccountStatus = AccountStatus.ACTIVE,
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            createdUserIds.Add(u.Id);

            db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = roleId });
            var st = new Staff
            {
                UserId = u.Id,
                HospitalId = hospital.Id,
                DepartmentId = department.Id,
                StaffCode = $"{prefix.ToUpperInvariant()}-{tag}"[..Math.Min(18, prefix.Length + 1 + tag.Length)],
                FullName = $"Nhân sự {prefix} {tag}",
                Status = "ACTIVE",
            };
            db.Staff.Add(st);
            await db.SaveChangesAsync();
            return (u, st);
        }

        var (admin1User, _) = await CreateStaffAsync("adm1", adminRole.Id);
        var (admin2User, _) = await CreateStaffAsync("adm2", adminRole.Id);
        var (recepUser, _) = await CreateStaffAsync("rec1", recepRole.Id);
        var (_, doc1Staff) = await CreateStaffAsync("doc1", docRole.Id);
        var (_, doc2Staff) = await CreateStaffAsync("doc2", docRole.Id);

        var doc1 = new Doctor
        {
            HospitalId = hospital.Id,
            StaffId = doc1Staff.Id,
            SpecialtyId = specialty.Id,
            DoctorCode = $"D1-{tag}",
            ProfessionalTitle = "BS.CKI",
            LicenseNumber = $"LIC1-{tag}",
            IsActive = true,
        };
        var doc2 = new Doctor
        {
            HospitalId = hospital.Id,
            StaffId = doc2Staff.Id,
            SpecialtyId = specialty.Id,
            DoctorCode = $"D2-{tag}",
            ProfessionalTitle = "BS.CKII",
            LicenseNumber = $"LIC2-{tag}",
            IsActive = true,
        };
        db.Doctors.AddRange(doc1, doc2);
        await db.SaveChangesAsync();

        var examService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = department.Id,
            SpecialtyId = specialty.Id,
            ServiceCode = $"EX-{tag}",
            Name = $"Khám Nội Tổng Hợp {tag}",
            ServiceType = "EXAMINATION",
            AllowsOnlineBooking = true,
            IsActive = true,
        };
        var diagService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = department.Id,
            SpecialtyId = specialty.Id,
            ServiceCode = $"DG-{tag}",
            Name = $"Siêu âm Ổ bụng {tag}",
            ServiceType = "DIAGNOSTIC",
            AllowsOnlineBooking = false,
            RequiresResultVerification = true,
            IsActive = true,
        };
        db.Services.AddRange(examService, diagService);
        await db.SaveChangesAsync();

        db.ServicePrices.AddRange(
            new ServicePrice
            {
                ServiceId = examService.Id,
                PriceAmount = 250_000m,
                EffectiveFrom = nowLocal.AddDays(-60),
                EffectiveTo = null,
            },
            new ServicePrice
            {
                ServiceId = diagService.Id,
                PriceAmount = 150_000m,
                EffectiveFrom = nowLocal.AddDays(-60),
                EffectiveTo = null,
            });

        db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = examService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = examService.Id, IsActive = true });

        var examSp = new QueueServicePoint
        {
            HospitalId = hospital.Id,
            Name = $"Điểm khám {tag}",
            PointType = "EXAMINATION",
            RoomId = room1.Id,
            DoctorId = doc1.Id,
            ServiceId = examService.Id,
            IsActive = true,
        };
        var diagSp = new QueueServicePoint
        {
            HospitalId = hospital.Id,
            Name = $"Điểm CLS {tag}",
            PointType = "DIAGNOSTIC",
            RoomId = room2.Id,
            ServiceId = diagService.Id,
            IsActive = true,
        };
        db.QueueServicePoints.AddRange(examSp, diagSp);
        await db.SaveChangesAsync();

        var policySvc = scope.ServiceProvider.GetRequiredService<IAppointmentPolicyService>();
        await policySvc.UpdateForAdminAsync(
            admin1User.Id,
            new UpdateAppointmentPolicyRequest
            {
                BookingWindowDays = 30,
                BookingLeadHours = 0,
                MaxActiveBookings = 5,
                RescheduleMinimumHours = 0,
                MaxReschedules = maxReschedules,
                CancelMinimumHours = 0,
                NoShowGraceMinutes = 15,
                NoShowMaxCount = 5,
                NoShowLookbackDays = 90,
                NoShowBlockDays = 0,
            },
            "127.0.0.1");

        return new IsolatedPhase04Fixture(
            Tag: tag,
            HospitalId: hospital.Id,
            DepartmentId: department.Id,
            SpecialtyId: specialty.Id,
            Room1Id: room1.Id,
            Room2Id: room2.Id,
            Doctor1Id: doc1.Id,
            Doctor1Code: doc1.DoctorCode,
            Doctor2Id: doc2.Id,
            Doctor2Code: doc2.DoctorCode,
            ExamServiceId: examService.Id,
            ExamServiceCode: examService.ServiceCode,
            DiagServiceId: diagService.Id,
            ExamServicePointId: examSp.Id,
            DiagServicePointId: diagSp.Id,
            Admin1UserId: admin1User.Id,
            Admin2UserId: admin2User.Id,
            ReceptionistUserId: recepUser.Id,
            CreatedUserIds: createdUserIds,
            CreatedPatientIds: []);
    }

    private async Task<(long UserId, long PatientId)> CreateVerifiedPatientAsync(
        IsolatedPhase04Fixture fx,
        string label)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
        var patientRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Patient);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var user = new User
        {
            Email = $"p04-pat-{label}-{fx.Tag.ToLowerInvariant()}-{Guid.NewGuid():N}@medflow.test",
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = patientRole.Id });

        var patient = new Patient
        {
            UserId = user.Id,
            PatientCode = $"P04{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            FullName = $"Bệnh nhân {label} {fx.Tag}",
            DateOfBirth = new DateOnly(1992, 4, 10),
            Gender = "MALE",
            Phone = "0912345678",
            Status = "ACTIVE",
            IdentificationHashHmac = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        lock (fx.CreatedUserIds)
        {
            fx.CreatedUserIds.Add(user.Id);
            fx.CreatedPatientIds.Add(patient.Id);
        }

        return (user.Id, patient.Id);
    }

    private async Task<ExaminationSlot> CreateSlotAsync(
        long serviceId,
        long? doctorId,
        long? roomId,
        DateTime startsAt,
        DateTime endsAt,
        int capacity = 3,
        string status = "OPEN")
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var slot = new ExaminationSlot
        {
            ServiceId = serviceId,
            DoctorId = doctorId,
            RoomId = roomId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Capacity = capacity,
            Status = status,
        };
        db.ExaminationSlots.Add(slot);
        await db.SaveChangesAsync();
        return slot;
    }

    private async Task<Appointment> SeedBookedAppointmentAsync(
        long patientId,
        long bookedByUserId,
        long slotId,
        string status = "BOOKED")
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
        var nowLocal = HospitalClock.GetNow(_clock);

        var appt = new Appointment
        {
            PatientId = patientId,
            SlotId = slotId,
            BookingChannel = "ONLINE",
            BookedByUserId = bookedByUserId,
            BookedAt = nowLocal.AddHours(-1),
            Status = status,
            ReasonForVisit = "Đau bụng âm ỉ cần khám nội khoa kiểm tra",
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();
        appt.AppointmentCode = $"APT-P04-{appt.Id:D6}";
        appt.RootAppointmentId = appt.Id;
        await db.SaveChangesAsync();
        return appt;
    }

    private async Task CleanupIsolatedFixtureAsync(IsolatedPhase04Fixture fx)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var slotIds = await db.ExaminationSlots
            .AsNoTracking()
            .Where(s => s.ServiceId == fx.ExamServiceId || s.ServiceId == fx.DiagServiceId ||
                        s.DoctorId == fx.Doctor1Id || s.DoctorId == fx.Doctor2Id ||
                        s.RoomId == fx.Room1Id || s.RoomId == fx.Room2Id)
            .Select(s => s.Id)
            .ToListAsync();

        var apptIds = await db.Appointments
            .AsNoTracking()
            .Where(a => (a.SlotId != null && slotIds.Contains(a.SlotId.Value)) ||
                        (a.PatientId != null && fx.CreatedPatientIds.Contains(a.PatientId.Value)))
            .Select(a => a.Id)
            .ToListAsync();

        var visitIds = await db.Visits
            .AsNoTracking()
            .Where(v => (v.AppointmentId != null && apptIds.Contains(v.AppointmentId.Value)) ||
                        (v.PatientId != null && fx.CreatedPatientIds.Contains(v.PatientId.Value)) ||
                        v.AssignedDoctorId == fx.Doctor1Id || v.AssignedDoctorId == fx.Doctor2Id)
            .Select(v => v.Id)
            .ToListAsync();

        var spIds = await db.QueueServicePoints
            .AsNoTracking()
            .Where(sp => sp.HospitalId == fx.HospitalId)
            .Select(sp => sp.Id)
            .ToListAsync();

        if (apptIds.Count > 0 || fx.CreatedPatientIds.Count > 0)
        {
            await db.Notifications
                .Where(n => (n.AppointmentId != null && apptIds.Contains(n.AppointmentId.Value)) ||
                            (n.PatientId != null && fx.CreatedPatientIds.Contains(n.PatientId.Value)))
                .ExecuteDeleteAsync();
        }

        // Stage E2 (migration 027): fk_queue_entries_diagnostic_order_visit + fk_queue_entries_prescription_visit
        // are ON DELETE RESTRICT. Clear diagnostic_orders.queue_entry_id + queue_fairness_states
        // .last_started_queue_entry_id back-pointers BEFORE deleting queue_entries.
        if (visitIds.Count > 0)
        {
            await db.DiagnosticOrders
                .Where(o => o.QueueEntryId != null && o.VisitId != null && visitIds.Contains(o.VisitId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.QueueEntryId, (long?)null));
        }

        if (spIds.Count > 0)
        {
            await db.QueueFairnessStates.Where(q => spIds.Contains(q.ServicePointId)).ExecuteDeleteAsync();
        }

        if (spIds.Count > 0 || apptIds.Count > 0 || visitIds.Count > 0)
        {
            // #863: payment_enrollment_intents.queue_entry_id + clinical_session_recovery_requests.queue_entry_id
            // are ON DELETE RESTRICT; intent rows now exist for auto-enrolled CLS orders, so they must go first.
            var queueIdsToDelete = await db.QueueEntries.AsNoTracking()
                .Where(q => (q.ServicePointId != null && spIds.Contains(q.ServicePointId.Value)) ||
                            (q.CurrentServicePointId != null && spIds.Contains(q.CurrentServicePointId.Value)) ||
                            (q.AppointmentId != null && apptIds.Contains(q.AppointmentId.Value)) ||
                            (q.VisitId != null && visitIds.Contains(q.VisitId.Value)))
                .Select(q => q.Id)
                .ToListAsync();
            if (queueIdsToDelete.Count > 0)
            {
                await db.PaymentEnrollmentIntents
                    .Where(i => i.QueueEntryId.HasValue && queueIdsToDelete.Contains(i.QueueEntryId.Value))
                    .ExecuteDeleteAsync();
                await db.ClinicalSessionRecoveryRequests
                    .Where(r => queueIdsToDelete.Contains(r.QueueEntryId))
                    .ExecuteDeleteAsync();
            }
            await db.QueueEntries
                .Where(q => (q.ServicePointId != null && spIds.Contains(q.ServicePointId.Value)) ||
                            (q.CurrentServicePointId != null && spIds.Contains(q.CurrentServicePointId.Value)) ||
                            (q.AppointmentId != null && apptIds.Contains(q.AppointmentId.Value)) ||
                            (q.VisitId != null && visitIds.Contains(q.VisitId.Value)))
                .ExecuteDeleteAsync();
        }

        if (visitIds.Count > 0)
        {
            await db.Payments.Where(p => p.VisitId != null && visitIds.Contains(p.VisitId.Value)).ExecuteDeleteAsync();
            await db.VisitCharges.Where(vc => vc.VisitId != null && visitIds.Contains(vc.VisitId.Value)).ExecuteDeleteAsync();
            await db.DiagnosticOrders.Where(o => o.VisitId != null && visitIds.Contains(o.VisitId.Value)).ExecuteDeleteAsync();
        }

        if (visitIds.Count > 0)
        {
            await db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
        }

        if (apptIds.Count > 0)
        {
            await db.Appointments
                .Where(a => apptIds.Contains(a.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.RescheduledFromAppointmentId, (long?)null)
                    .SetProperty(a => a.RootAppointmentId, (long?)null));
            await db.Appointments.Where(a => apptIds.Contains(a.Id)).ExecuteDeleteAsync();
        }

        if (slotIds.Count > 0)
        {
            await db.ExaminationSlots.Where(s => slotIds.Contains(s.Id)).ExecuteDeleteAsync();
        }

        if (spIds.Count > 0)
        {
            await db.QueueServicePoints.Where(sp => spIds.Contains(sp.Id)).ExecuteDeleteAsync();
        }

        await db.DoctorServiceAuthorizations
            .Where(a => a.DoctorId == fx.Doctor1Id || a.DoctorId == fx.Doctor2Id)
            .ExecuteDeleteAsync();

        await db.ServicePrices
            .Where(p => p.ServiceId == fx.ExamServiceId || p.ServiceId == fx.DiagServiceId)
            .ExecuteDeleteAsync();

        await db.Services.Where(s => s.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.Doctors.Where(d => d.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.HospitalRooms.Where(r => r.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.HospitalPolicies.Where(hp => hp.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.AuditLogs
            .Where(al => al.HospitalId == fx.HospitalId ||
                         (al.ActorUserId != null && fx.CreatedUserIds.Contains(al.ActorUserId.Value)))
            .ExecuteDeleteAsync();
        await db.Staff.Where(st => st.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.Departments.Where(d => d.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.Specialties.Where(sp => sp.Id == fx.SpecialtyId).ExecuteDeleteAsync();

        if (fx.CreatedPatientIds.Count > 0)
        {
            await db.Patients.Where(p => fx.CreatedPatientIds.Contains(p.Id)).ExecuteDeleteAsync();
        }

        if (fx.CreatedUserIds.Count > 0)
        {
            await db.UserRoles.Where(ur => fx.CreatedUserIds.Contains(ur.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(u => fx.CreatedUserIds.Contains(u.Id)).ExecuteDeleteAsync();
        }

        await db.Hospitals.Where(h => h.Id == fx.HospitalId).ExecuteDeleteAsync();
    }

    // =========================================================================
    // GROUP 1 (BUG-P4-01 & BUG-P4-04): All 5 Appointment Race Pairs on PostgreSQL
    // =========================================================================

    [Fact]
    public async Task Race1_CheckInVsCancel_ExactlyOneWins_LoserGets409_ConsistentDbState()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var (patUserId, patId) = await CreateVerifiedPatientAsync(fx, "r1");
            var slot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 26, 14, 0, 0),
                new DateTime(2026, 11, 26, 14, 30, 0));
            var appt = await SeedBookedAppointmentAsync(patId, patUserId, slot.Id);

            using var ready = new SemaphoreSlim(0, 2);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var checkInTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICheckInScheduledPatientService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.CheckInScheduledPatientAsync(
                        fx.ReceptionistUserId,
                        appt.Id.ToString(CultureInfo.InvariantCulture),
                        new CheckInScheduledPatientRequest(IsIdentityVerified: true));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var cancelTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IAppointmentCancelService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.CancelMyAppointmentAsync(
                        patUserId,
                        appt.Id.ToString(CultureInfo.InvariantCulture),
                        new CancelAppointmentRequest { CancellationReason = "Người bệnh bận công tác đột xuất" },
                        "127.0.0.1");
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            go.SetResult();

            var outcomes = await Task.WhenAll(checkInTask, cancelTask);
            var checkInErr = await checkInTask;
            var cancelErr = await cancelTask;
            Assert.True(cancelErr is null or ConflictException, $"Cancel threw unexpected {cancelErr?.GetType().Name}: {cancelErr?.Message}");
            Assert.True(
                checkInErr is null or ConflictException ||
                PostgresConcurrencyException.TryGetSqlState(checkInErr!) is not null,
                $"CheckIn threw unexpected {checkInErr?.GetType().Name}: {checkInErr?.Message}");
            Assert.Equal(1, outcomes.Count(e => e is null));
            Assert.Equal(1, outcomes.Count(e => e is not null));

            await using var verifyScope = _app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var reloaded = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appt.Id);
            var activeVisits = await db.Visits.AsNoTracking()
                .Where(v => v.AppointmentId == appt.Id && v.Status != "CANCELLED")
                .ToListAsync();

            if (reloaded.Status == "CHECKED_IN")
            {
                Assert.Single(activeVisits);
            }
            else
            {
                Assert.Equal("CANCELLED", reloaded.Status);
                Assert.Empty(activeVisits);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    [Fact]
    public async Task Race2_CheckInVsReschedule_ExactlyOneWins_LoserGets409_NeverActiveVisitPlusReplacement()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var (patUserId, patId) = await CreateVerifiedPatientAsync(fx, "r2");
            var currentSlot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 26, 14, 0, 0),
                new DateTime(2026, 11, 26, 14, 30, 0));
            var targetSlot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 27, 9, 0, 0),
                new DateTime(2026, 11, 27, 9, 30, 0));
            var appt = await SeedBookedAppointmentAsync(patId, patUserId, currentSlot.Id);

            using var ready = new SemaphoreSlim(0, 2);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var checkInTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICheckInScheduledPatientService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.CheckInScheduledPatientAsync(
                        fx.ReceptionistUserId,
                        appt.Id.ToString(CultureInfo.InvariantCulture),
                        new CheckInScheduledPatientRequest(IsIdentityVerified: true));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var rescheduleTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IAppointmentRescheduleService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.RescheduleAsync(
                        patUserId,
                        appt.Id.ToString(CultureInfo.InvariantCulture),
                        new RescheduleAppointmentRequest
                        {
                            TargetSlotId = targetSlot.Id.ToString(CultureInfo.InvariantCulture),
                            RescheduleNote = "Đổi sang ngày hôm sau",
                        },
                        "127.0.0.1");
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            go.SetResult();

            var outcomes = await Task.WhenAll(checkInTask, rescheduleTask);
            var checkInErr = await checkInTask;
            var rescheduleErr = await rescheduleTask;
            Assert.True(rescheduleErr is null or ConflictException, $"Reschedule threw unexpected {rescheduleErr?.GetType().Name}: {rescheduleErr?.Message}");
            Assert.True(
                checkInErr is null or ConflictException ||
                PostgresConcurrencyException.TryGetSqlState(checkInErr!) is not null,
                $"CheckIn threw unexpected {checkInErr?.GetType().Name}: {checkInErr?.Message}");
            Assert.Equal(1, outcomes.Count(e => e is null));
            Assert.Equal(1, outcomes.Count(e => e is not null));

            await using var verifyScope = _app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var original = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appt.Id);
            var activeVisits = await db.Visits.AsNoTracking()
                .CountAsync(v => v.AppointmentId == appt.Id && v.Status != "CANCELLED");
            var activeReplacements = await db.Appointments.AsNoTracking()
                .CountAsync(a => a.SlotId == targetSlot.Id && a.Status != "CANCELLED");

            if (original.Status == "CHECKED_IN")
            {
                Assert.Equal(1, activeVisits);
                Assert.Equal(0, activeReplacements);
            }
            else
            {
                Assert.Equal("CANCELLED", original.Status);
                Assert.Equal(0, activeVisits);
                Assert.Equal(1, activeReplacements);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    [Fact]
    public async Task Race3_CancelVsReschedule_ExactlyOneWins_LoserGets409_ConsistentDbState()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var (patUserId, patId) = await CreateVerifiedPatientAsync(fx, "r3");
            var currentSlot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 27, 8, 0, 0),
                new DateTime(2026, 11, 27, 8, 30, 0));
            var targetSlot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 28, 8, 0, 0),
                new DateTime(2026, 11, 28, 8, 30, 0));
            var appt = await SeedBookedAppointmentAsync(patId, patUserId, currentSlot.Id);

            using var ready = new SemaphoreSlim(0, 2);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var cancelTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IAppointmentCancelService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.CancelMyAppointmentAsync(
                        patUserId,
                        appt.Id.ToString(CultureInfo.InvariantCulture),
                        new CancelAppointmentRequest { CancellationReason = "Chủ động hủy lịch khám ngoại trú" },
                        "127.0.0.1");
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var rescheduleTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IAppointmentRescheduleService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.RescheduleAsync(
                        patUserId,
                        appt.Id.ToString(CultureInfo.InvariantCulture),
                        new RescheduleAppointmentRequest
                        {
                            TargetSlotId = targetSlot.Id.ToString(CultureInfo.InvariantCulture),
                            RescheduleNote = "Đổi sang ngày 28/11",
                        },
                        "127.0.0.1");
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            go.SetResult();

            var outcomes = await Task.WhenAll(cancelTask, rescheduleTask);
            var rescheduleErr = await rescheduleTask;
            Assert.Equal(1, outcomes.Count(e => e is null));
            Assert.Equal(1, outcomes.Count(e => e is ConflictException));

            await using var verifyScope = _app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var original = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appt.Id);
            Assert.Equal("CANCELLED", original.Status);

            var replacementCount = await db.Appointments.AsNoTracking()
                .CountAsync(a => a.SlotId == targetSlot.Id && a.Status != "CANCELLED");
            Assert.Equal(rescheduleErr is null ? 1 : 0, replacementCount);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    [Fact]
    public async Task Race4_RescheduleVsReschedule_EnforcesLineageMaxReschedulesUnderRootLock_ExactlyOneWins()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(maxReschedules: 2);
        try
        {
            var (patUserId, patId) = await CreateVerifiedPatientAsync(fx, "r4");
            var slot0 = await CreateSlotAsync(fx.ExamServiceId, fx.Doctor1Id, fx.Room1Id,
                new DateTime(2026, 11, 27, 8, 0, 0), new DateTime(2026, 11, 27, 8, 30, 0));
            var slot1 = await CreateSlotAsync(fx.ExamServiceId, fx.Doctor1Id, fx.Room1Id,
                new DateTime(2026, 11, 28, 8, 0, 0), new DateTime(2026, 11, 28, 8, 30, 0));
            var slot2A = await CreateSlotAsync(fx.ExamServiceId, fx.Doctor1Id, fx.Room1Id,
                new DateTime(2026, 11, 29, 8, 0, 0), new DateTime(2026, 11, 29, 8, 30, 0));
            var slot2B = await CreateSlotAsync(fx.ExamServiceId, fx.Doctor1Id, fx.Room1Id,
                new DateTime(2026, 11, 30, 8, 0, 0), new DateTime(2026, 11, 30, 8, 30, 0));
            var slot3 = await CreateSlotAsync(fx.ExamServiceId, fx.Doctor1Id, fx.Room1Id,
                new DateTime(2026, 12, 1, 8, 0, 0), new DateTime(2026, 12, 1, 8, 30, 0));

            var rootAppt = await SeedBookedAppointmentAsync(patId, patUserId, slot0.Id);

            // First reschedule: rootAppt -> firstRescheduled (lineage rescheduleCount = 1 of 2)
            AppointmentDetailDto firstRescheduled;
            await using (var s1 = _app.Services.CreateAsyncScope())
            {
                var svc = s1.ServiceProvider.GetRequiredService<IAppointmentRescheduleService>();
                firstRescheduled = await svc.RescheduleAsync(
                    patUserId,
                    rootAppt.Id.ToString(CultureInfo.InvariantCulture),
                    new RescheduleAppointmentRequest
                    {
                        TargetSlotId = slot1.Id.ToString(CultureInfo.InvariantCulture),
                        RescheduleNote = "Đổi lịch lần 1",
                    },
                    "127.0.0.1");
            }

            // Now fire TWO concurrent reschedules on firstRescheduled -> slot2A vs slot2B!
            using var ready = new SemaphoreSlim(0, 2);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Exception?> RunRescheduleAsync(long targetSlotId, string note)
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IAppointmentRescheduleService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.RescheduleAsync(
                        patUserId,
                        firstRescheduled.Id,
                        new RescheduleAppointmentRequest
                        {
                            TargetSlotId = targetSlotId.ToString(CultureInfo.InvariantCulture),
                            RescheduleNote = note,
                        },
                        "127.0.0.1");
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var tA = RunRescheduleAsync(slot2A.Id, "Đổi song song A");
            var tB = RunRescheduleAsync(slot2B.Id, "Đổi song song B");

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            go.SetResult();

            var outcomes = await Task.WhenAll(tA, tB);
            Assert.Equal(1, outcomes.Count(e => e is null));
            Assert.Equal(1, outcomes.Count(e => e is ConflictException));

            await using var verifyScope = _app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var lineageRows = await db.Appointments.AsNoTracking()
                .Where(a => a.RootAppointmentId == rootAppt.Id)
                .OrderBy(a => a.Id)
                .ToListAsync();

            Assert.Equal(3, lineageRows.Count);
            var activeWinner = Assert.Single(lineageRows, a => a.Status == "BOOKED");
            Assert.Equal(rootAppt.Id, activeWinner.RootAppointmentId);
            Assert.Equal(long.Parse(firstRescheduled.Id, CultureInfo.InvariantCulture), activeWinner.RescheduledFromAppointmentId);

            // Lineage is now at MaxReschedules (2) -> any 3rd reschedule attempt must throw 409 ConflictException!
            var rescheduleSvc = verifyScope.ServiceProvider.GetRequiredService<IAppointmentRescheduleService>();
            await Assert.ThrowsAsync<ConflictException>(() =>
                rescheduleSvc.RescheduleAsync(
                    patUserId,
                    activeWinner.Id.ToString(CultureInfo.InvariantCulture),
                    new RescheduleAppointmentRequest { TargetSlotId = slot3.Id.ToString(CultureInfo.InvariantCulture) },
                    "127.0.0.1"));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    [Fact]
    public async Task Race5_NoShowCronjobVsCheckIn_SerializedViaRowLocks_NeverNoShowWithActiveVisit()
    {
        // 2026-11-26 10:00:00 VN: morning slot 08:00-08:30 is BOTH same-day check-in eligible AND overdue (> 08:45) for NO_SHOW!
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var (patUserId, patId) = await CreateVerifiedPatientAsync(fx, "r5");
            var overdueSameDaySlot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 26, 8, 0, 0),
                new DateTime(2026, 11, 26, 8, 30, 0));
            var appt = await SeedBookedAppointmentAsync(patId, patUserId, overdueSameDaySlot.Id);

            var processor = new AppointmentLifecycleProcessor(
                _app.Services.GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<ScheduleJobsOptions>(new ScheduleJobsOptions { BatchSize = 50 }),
                _clock,
                NullLogger<AppointmentLifecycleProcessor>.Instance);

            using var ready = new SemaphoreSlim(0, 2);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var checkInTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICheckInScheduledPatientService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.CheckInScheduledPatientAsync(
                        fx.ReceptionistUserId,
                        appt.Id.ToString(CultureInfo.InvariantCulture),
                        new CheckInScheduledPatientRequest(IsIdentityVerified: true));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var cronTask = Task.Run(async () =>
            {
                ready.Release();
                await go.Task;
                try
                {
                    await processor.ProcessCycleAsync(CancellationToken.None);
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            go.SetResult();

            await Task.WhenAll(checkInTask, cronTask);
            var checkInErr = await checkInTask;
            var cronErr = await cronTask;
            Assert.Null(cronErr);
            Assert.True(
                checkInErr is null or ConflictException ||
                PostgresConcurrencyException.TryGetSqlState(checkInErr!) is not null,
                $"CheckIn threw unexpected {checkInErr?.GetType().Name}: {checkInErr?.Message}");

            await using var verifyScope = _app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var reloaded = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appt.Id);
            var activeVisits = await db.Visits.AsNoTracking()
                .CountAsync(v => v.AppointmentId == appt.Id && v.Status != "CANCELLED");

            if (checkInErr is null)
            {
                Assert.Equal("CHECKED_IN", reloaded.Status);
                Assert.Equal(1, activeVisits);
            }
            else
            {
                Assert.Equal("NO_SHOW", reloaded.Status);
                Assert.Equal(0, activeVisits);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 2 (BUG-P4-03): Service-Mode Parallel Room Contention & Doctor-Mode Strictness
    // =========================================================================

    [Fact]
    public async Task Group2_ServiceMode6ConcurrentPatientsAcross2ParallelRooms_AssignsExactly2_AndDoctorModeNeverSwitchesDoctor()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var slotRoom1 = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 27, 9, 0, 0),
                new DateTime(2026, 11, 27, 9, 30, 0),
                capacity: 1);
            var slotRoom2 = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor2Id,
                fx.Room2Id,
                new DateTime(2026, 11, 27, 9, 0, 0),
                new DateTime(2026, 11, 27, 9, 30, 0),
                capacity: 1);

            var patients = new List<(long UserId, long PatientId)>();
            for (var i = 0; i < 6; i++)
            {
                patients.Add(await CreateVerifiedPatientAsync(fx, $"sm{i}"));
            }

            using var ready = new SemaphoreSlim(0, 6);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = patients.Select(p => Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IAppointmentBookingService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.CreateAsync(
                        p.UserId,
                        new CreateAppointmentRequest
                        {
                            Mode = "SERVICE",
                            PatientId = p.PatientId.ToString(CultureInfo.InvariantCulture),
                            ServiceId = fx.ExamServiceCode,
                            DateIso = "2026-11-27",
                            SlotOrShiftId = slotRoom1.Id.ToString(CultureInfo.InvariantCulture),
                            SymptomNote = "Khám nội khoa tổng quát theo dịch vụ",
                        },
                        "127.0.0.1");
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            })).ToArray();

            for (var i = 0; i < 6; i++)
            {
                await ready.WaitAsync(TimeSpan.FromSeconds(5));
            }
            go.SetResult();

            var outcomes = await Task.WhenAll(tasks);
            Assert.Equal(2, outcomes.Count(e => e is null));
            Assert.Equal(4, outcomes.Count(e => e is ConflictException));

            await using var verifyScope = _app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var room1Count = await db.Appointments.AsNoTracking()
                .CountAsync(a => a.SlotId == slotRoom1.Id && a.Status != "CANCELLED");
            var room2Count = await db.Appointments.AsNoTracking()
                .CountAsync(a => a.SlotId == slotRoom2.Id && a.Status != "CANCELLED");
            Assert.Equal(1, room1Count);
            Assert.Equal(1, room2Count);

            // Doctor-mode strictness: if Doctor 1's slot at 10:00 is full, Doctor-mode MUST throw 409 even if Doctor 2's 10:00 slot is OPEN!
            var doc1FullSlot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 28, 10, 0, 0),
                new DateTime(2026, 11, 28, 10, 30, 0),
                capacity: 1);
            var doc2OpenSlot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor2Id,
                fx.Room2Id,
                new DateTime(2026, 11, 28, 10, 0, 0),
                new DateTime(2026, 11, 28, 10, 30, 0),
                capacity: 3);

            await SeedBookedAppointmentAsync(patients[0].PatientId, patients[0].UserId, doc1FullSlot.Id);

            var bookingSvc = verifyScope.ServiceProvider.GetRequiredService<IAppointmentBookingService>();
            await Assert.ThrowsAsync<ConflictException>(() =>
                bookingSvc.CreateAsync(
                    patients[5].UserId,
                    new CreateAppointmentRequest
                    {
                        Mode = "DOCTOR",
                        PatientId = patients[5].PatientId.ToString(CultureInfo.InvariantCulture),
                        DoctorId = fx.Doctor1Code,
                        ServiceId = fx.ExamServiceCode,
                        DateIso = "2026-11-28",
                        SlotOrShiftId = doc1FullSlot.Id.ToString(CultureInfo.InvariantCulture),
                        SymptomNote = "Chọn đích danh Bác sĩ 1",
                    },
                    "127.0.0.1"));

            Assert.Equal(0, await db.Appointments.AsNoTracking().CountAsync(a => a.SlotId == doc2OpenSlot.Id));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 3 (BUG-P4-02): Cronjob Reminder Starvation & FAILED Outbox Non-Duplication
    // =========================================================================

    [Fact]
    public async Task Group3_ReminderCronjob_150UpcomingAppointmentsBatch50WithFailedRows_NoStarvationAndZeroDuplicates()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var (patUserId, patId) = await CreateVerifiedPatientAsync(fx, "cron");
            var nowLocal = HospitalClock.GetNow(_clock);

            var slot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                nowLocal.AddHours(3),
                nowLocal.AddHours(4),
                capacity: 200);

            var apptIds = new List<long>(150);
            await using (var seedScope = _app.Services.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var appts = Enumerable.Range(1, 150).Select(idx => new Appointment
                {
                    PatientId = patId,
                    SlotId = slot.Id,
                    AppointmentCode = $"APT-CRN-{fx.Tag}-{idx:D3}",
                    BookingChannel = "ONLINE",
                    BookedByUserId = patUserId,
                    BookedAt = nowLocal.AddMinutes(-idx),
                    Status = "BOOKED",
                    ReasonForVisit = "Khám định kỳ kiểm tra sức khỏe",
                }).ToList();

                db.Appointments.AddRange(appts);
                await db.SaveChangesAsync();

                apptIds.AddRange(appts.OrderBy(a => a.Id).Select(a => a.Id));

                // Pre-insert 60 FAILED reminder rows on the earliest 60 appointments (greater than BatchSize = 50,
                // and 60% of Take(batchSize * 2 = 100) which previously starved later rows indefinitely!).
                var failedReminders = apptIds.Take(60).Select(aId => new Notification
                {
                    PatientId = patId,
                    AppointmentId = aId,
                    Channel = Notification.ChannelEmail,
                    Recipient = "failed@medflow.test",
                    Subject = "Nhắc lịch khám",
                    Body = "Nội dung nhắc lịch",
                    NotificationType = Notification.TypeAppointmentReminder,
                    Status = Notification.StatusFailed,
                    RetryCount = 3,
                    LastError = "SMTP permanent failure",
                    ScheduledAt = nowLocal.AddMinutes(-10),
                    CreatedAt = nowLocal.AddMinutes(-10),
                });

                db.Notifications.AddRange(failedReminders);
                await db.SaveChangesAsync();
            }

            var processor = new AppointmentLifecycleProcessor(
                _app.Services.GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<ScheduleJobsOptions>(new ScheduleJobsOptions
                {
                    BatchSize = 50,
                    DefaultReminderHours = 24,
                }),
                _clock,
                NullLogger<AppointmentLifecycleProcessor>.Instance);

            var cycle1 = await processor.ProcessCycleAsync();
            var cycle2 = await processor.ProcessCycleAsync();
            var cycle3 = await processor.ProcessCycleAsync();

            Assert.True(cycle1.RemindersQueuedCount + cycle2.RemindersQueuedCount >= 90);
            Assert.Equal(0, cycle3.RemindersQueuedCount);

            await using var verifyScope = _app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

            var remindersForFixture = await verifyDb.Notifications
                .AsNoTracking()
                .Where(n => n.AppointmentId != null &&
                            apptIds.Contains(n.AppointmentId.Value) &&
                            n.NotificationType == Notification.TypeAppointmentReminder)
                .ToListAsync();

            // Exact 150 rows total: 60 untouched FAILED rows + 90 newly queued PENDING rows (0 duplicates, 0 starved!)
            Assert.Equal(150, remindersForFixture.Count);
            Assert.Equal(150, remindersForFixture.Select(r => r.AppointmentId!.Value).Distinct().Count());
            Assert.Equal(60, remindersForFixture.Count(r => r.Status == Notification.StatusFailed));
            Assert.Equal(90, remindersForFixture.Count(r => r.Status == Notification.StatusPending));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 4 (BUG-P4-05): Concurrent Booking vs Admin Deactivation & Active Queue Guards
    // =========================================================================

    [Fact]
    public async Task Group4_ConcurrentBookingVsDoctorDeactivation_AndActiveQueueBlocksAdminPutAndDeactivate()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var (patUserId, patId) = await CreateVerifiedPatientAsync(fx, "adm");
            var futureSlot = await CreateSlotAsync(
                fx.ExamServiceId,
                fx.Doctor1Id,
                fx.Room1Id,
                new DateTime(2026, 11, 27, 10, 0, 0),
                new DateTime(2026, 11, 27, 10, 30, 0),
                capacity: 2);

            using var ready = new SemaphoreSlim(0, 2);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var bookTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IAppointmentBookingService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.CreateAsync(
                        patUserId,
                        new CreateAppointmentRequest
                        {
                            Mode = "DOCTOR",
                            PatientId = patId.ToString(CultureInfo.InvariantCulture),
                            DoctorId = fx.Doctor1Code,
                            ServiceId = fx.ExamServiceCode,
                            DateIso = "2026-11-27",
                            SlotOrShiftId = futureSlot.Id.ToString(CultureInfo.InvariantCulture),
                            SymptomNote = "Khám nội khoa trước khi bác sĩ nghỉ",
                        },
                        "127.0.0.1");
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var deactivateTask = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IDoctorAdminService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.DeactivateDoctorAsync(
                        fx.Admin1UserId,
                        fx.Doctor1Id.ToString(CultureInfo.InvariantCulture),
                        new DeactivateDoctorRequest(Reason: "Đi công tác", ConfirmUnderstood: false),
                        "127.0.0.1");
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            go.SetResult();

            var outcomes = await Task.WhenAll(bookTask, deactivateTask);
            var bookErr = await bookTask;
            var deactivateErr = await deactivateTask;
            Assert.All(outcomes, ex => Assert.True(ex is null or ConflictException or NotFoundException));
            Assert.Equal(1, outcomes.Count(e => e is null));
            Assert.Equal(1, outcomes.Count(e => e is ConflictException or NotFoundException));

            // Re-activate Doctor 1 and ExamServicePoint if deactivation won, then verify active today QueueEntry blocks ALL 4 admin services!
            await using var guardScope = _app.Services.CreateAsyncScope();
            var db = guardScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var docStateAfterRace = await db.Doctors.AsNoTracking().SingleAsync(d => d.Id == fx.Doctor1Id);
            var activeBookingsAfterRace = await db.Appointments.AsNoTracking()
                .CountAsync(a => a.SlotId == futureSlot.Id && a.Status != "CANCELLED");

            if (deactivateErr is null)
            {
                Assert.False(docStateAfterRace.IsActive);
                Assert.Equal(0, activeBookingsAfterRace);
            }
            else
            {
                Assert.IsType<ConflictException>(deactivateErr);
                Assert.Null(bookErr);
                Assert.True(docStateAfterRace.IsActive);
                Assert.Equal(1, activeBookingsAfterRace);
            }
            await db.Doctors.Where(d => d.Id == fx.Doctor1Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, true));
            await db.QueueServicePoints
                .Where(sp => sp.Id == fx.ExamServicePointId || sp.Id == fx.DiagServicePointId)
                .ExecuteUpdateAsync(s => s.SetProperty(sp => sp.IsActive, true));

            var todayVn = new DateOnly(2026, 11, 26);
            db.QueueEntries.AddRange(
                new QueueEntry
                {
                    ServicePointId = fx.ExamServicePointId,
                    CurrentServicePointId = fx.ExamServicePointId,
                    QueueDate = todayVn,
                    Status = "WAITING",
                },
                new QueueEntry
                {
                    ServicePointId = fx.DiagServicePointId,
                    CurrentServicePointId = fx.DiagServicePointId,
                    QueueDate = todayVn,
                    Status = "IN_PROGRESS",
                });
            await db.SaveChangesAsync();

            var docAdmin = guardScope.ServiceProvider.GetRequiredService<IDoctorAdminService>();
            var roomAdmin = guardScope.ServiceProvider.GetRequiredService<IExaminationRoomService>();
            var examAdmin = guardScope.ServiceProvider.GetRequiredService<IExaminationServiceAdminService>();
            var diagAdmin = guardScope.ServiceProvider.GetRequiredService<IDiagnosticServiceAdminService>();

            await Assert.ThrowsAsync<ConflictException>(() =>
                docAdmin.UpdateDoctorAsync(
                    fx.Admin1UserId,
                    fx.Doctor1Id,
                    new UpdateDoctorAdminRequest(
                        FullName: $"Nhân sự doc1 {fx.Tag}",
                        ProfessionalTitle: "BS.CKI",
                        LicenseNumber: $"LIC1-{fx.Tag}",
                        DepartmentId: fx.DepartmentId,
                        SpecialtyId: fx.SpecialtyId,
                        IsActive: false)));

            await Assert.ThrowsAsync<ConflictException>(() =>
                roomAdmin.UpdateExaminationRoomAsync(
                    fx.Admin1UserId,
                    fx.Room1Id,
                    new UpdateExaminationRoomRequest(
                        RoomCode: $"R1-{fx.Tag}",
                        Name: $"Buồng khám 1 {fx.Tag}",
                        DepartmentId: fx.DepartmentId,
                        RoomType: "EXAMINATION",
                        Location: "Tầng 1",
                        Status: "MAINTENANCE")));

            await Assert.ThrowsAsync<ConflictException>(() =>
                examAdmin.UpdateExaminationServiceAsync(
                    fx.Admin1UserId,
                    fx.ExamServiceId,
                    new UpdateExaminationServiceRequest(
                        Name: $"Khám Nội Tổng Hợp {fx.Tag}",
                        ServiceType: "EXAMINATION",
                        DepartmentId: fx.DepartmentId,
                        SpecialtyId: fx.SpecialtyId,
                        IsActive: false)));

            await Assert.ThrowsAsync<ConflictException>(() =>
                diagAdmin.DeactivateDiagnosticServiceAsync(
                    fx.Admin1UserId,
                    fx.DiagServiceId,
                    new DeactivateDiagnosticServiceAdminRequest(Reason: "Bảo trì thiết bị cận lâm sàng"),
                    "127.0.0.1",
                    CancellationToken.None));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 5 (BUG-P4-06): Concurrent ServicePrice Updates & Snapshot Immutability
    // =========================================================================

    [Fact]
    public async Task Group5_ConcurrentAdminServicePriceUpdates_SerializedZeroOverlap_AndPreservesVisitChargeAndPaymentSnapshots()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var (_, patId) = await CreateVerifiedPatientAsync(fx, "prc");
            var nowLocal = HospitalClock.GetNow(_clock);

            long visitChargeId;
            long paymentId;
            await using (var seedScope = _app.Services.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var visit = new Visit
                {
                    PatientId = patId,
                    AssignedDoctorId = fx.Doctor1Id,
                    ExaminationRoomId = fx.Room1Id,
                    Status = "WAITING",
                    ReasonForVisit = "Khám kiểm tra nội khoa",
                    CheckedInAt = nowLocal.AddMinutes(-20),
                };
                db.Visits.Add(visit);
                await db.SaveChangesAsync();

                var charge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 250_000m, nowLocal.AddMinutes(-19));
                db.VisitCharges.Add(charge);
                await db.SaveChangesAsync();

                var payment = Payment.CreateForCharge(visit.Id, charge.Id, 250_000m, nowLocal.AddMinutes(-18), status: "PAID");
                payment.PaymentCode = $"PAY-P04-{fx.Tag}";
                payment.PaidAt = nowLocal.AddMinutes(-17);
                db.Payments.Add(payment);
                await db.SaveChangesAsync();

                visitChargeId = charge.Id;
                paymentId = payment.Id;
            }

            using var ready = new SemaphoreSlim(0, 2);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Exception?> RunPriceUpdateAsync(long adminUserId, decimal amount, string decision)
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IServicePriceAdminService>();
                ready.Release();
                await go.Task;
                try
                {
                    await svc.UpdateServicePriceAsync(
                        adminUserId,
                        fx.ExamServiceCode,
                        new UpdateServicePriceAdminRequest(
                            PriceAmount: amount,
                            ValidFrom: "2026-11-26",
                            DecisionNumber: decision,
                            AppliedBy: "Hội đồng Giá"),
                        "127.0.0.1");
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var t1 = RunPriceUpdateAsync(fx.Admin1UserId, 320_000m, "QĐ-P04-A");
            var t2 = RunPriceUpdateAsync(fx.Admin2UserId, 360_000m, "QĐ-P04-B");

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            go.SetResult();

            var outcomes = await Task.WhenAll(t1, t2);
            Assert.All(outcomes, ex => Assert.True(ex is null or ConflictException));
            Assert.True(outcomes.Count(e => e is null) >= 1);

            await using var verifyScope = _app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

            var prices = await verifyDb.ServicePrices
                .AsNoTracking()
                .Where(p => p.ServiceId == fx.ExamServiceId)
                .OrderBy(p => p.EffectiveFrom)
                .ThenBy(p => p.Id)
                .ToListAsync();

            // Strictly <= 1 open-ended row, and zero overlapping positive-length [EffectiveFrom, EffectiveTo) windows!
            Assert.Single(prices, p => p.EffectiveTo is null);
            var positiveWindows = prices
                .Where(p => p.EffectiveTo is null || p.EffectiveTo > p.EffectiveFrom)
                .OrderBy(p => p.EffectiveFrom)
                .ToList();

            for (var i = 0; i < positiveWindows.Count - 1; i++)
            {
                var currentEnd = positiveWindows[i].EffectiveTo ?? DateTime.MaxValue;
                var nextStart = positiveWindows[i + 1].EffectiveFrom ?? DateTime.MinValue;
                Assert.True(currentEnd <= nextStart, $"Detected overlapping ServicePrice windows: [{positiveWindows[i].EffectiveFrom}, {positiveWindows[i].EffectiveTo}) vs [{positiveWindows[i + 1].EffectiveFrom}, {positiveWindows[i + 1].EffectiveTo}).");
            }

            // Snapshot immutability: existing VisitCharge and Payment MUST remain 250,000 VND!
            var savedCharge = await verifyDb.VisitCharges.AsNoTracking().SingleAsync(vc => vc.Id == visitChargeId);
            var savedPayment = await verifyDb.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
            Assert.Equal(250_000m, savedCharge.UnitPrice);
            Assert.Equal(250_000m, savedPayment.Amount);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }
}
