using System.Globalization;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

internal static class AppointmentBookingRules
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public const string CancelledStatus = "CANCELLED";
    public const string BookedStatus = "BOOKED";
    public const string OnlineChannel = "ONLINE";
    public const string NoShowStatus = "NO_SHOW";

    public static readonly string[] InactiveStatuses = { "CANCELLED", "COMPLETED", "NO_SHOW", "CHECKED_IN" };
    public static readonly TimeSpan AfternoonShiftStart = new(12, 0, 0);

    public sealed record OwnedPatients(Patient Self, IReadOnlyList<PatientFamilyLink> FamilyLinks);

    public static DateTime GetVietnamNow(TimeProvider timeProvider)
        => DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime.Add(VietnamOffset), DateTimeKind.Unspecified);

    public static async Task<long> ResolveHospitalIdAsync(
        IApplicationDbContext db,
        long serviceId,
        long? doctorId,
        CancellationToken ct)
    {
        var hospitalId = await db.Services
            .AsNoTracking()
            .Where(s => s.Id == serviceId)
            .Select(s => s.HospitalId)
            .FirstOrDefaultAsync(ct);

        if (hospitalId is null && doctorId.HasValue)
        {
            var docId = doctorId.Value;
            hospitalId = await db.Doctors
                .AsNoTracking()
                .Where(d => d.Id == docId)
                .Select(d => d.HospitalId)
                .FirstOrDefaultAsync(ct);
        }

        return hospitalId ?? 0L;
    }

    public static async Task<OwnedPatients> LoadOwnedPatientsAsync(
        IApplicationDbContext db,
        long userId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var self = await db.Patients
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (self is null)
            throw new NotFoundException("Patient", userId);

        if (self.User is null || self.User.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đã bị khóa.");

        if (!string.Equals(self.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Hồ sơ bệnh nhân đang ngừng hoạt động.");

        var familyLinks = await db.PatientFamilyLinks
            .AsNoTracking()
            .Include(l => l.MemberPatient)
            .Where(l =>
                l.OwnerPatientId == self.Id &&
                l.VerificationStatus != null &&
                l.VerificationStatus.ToUpper() == "VERIFIED" &&
                l.RevokedAt == null &&
                (l.ValidUntil == null || l.ValidUntil > nowLocal) &&
                l.MemberPatient != null &&
                l.MemberPatient.Status != null &&
                l.MemberPatient.Status.ToUpper() == "ACTIVE")
            .OrderBy(l => l.MemberPatientId)
            .ToListAsync(ct);

        return new OwnedPatients(self, familyLinks);
    }

    public static async Task<string> ComputeQueueNumberAsync(
        IApplicationDbContext db,
        long appointmentId,
        ExaminationSlot slot,
        CancellationToken ct)
    {
        var dayStart = slot.StartsAt!.Value.Date;
        var dayEnd = dayStart.AddDays(1);

        var query = db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.Id <= appointmentId &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.StartsAt >= dayStart &&
                a.Slot.StartsAt < dayEnd);

        if (slot.DoctorId.HasValue)
        {
            var doctorId = slot.DoctorId.Value;
            query = query.Where(a => a.Slot!.DoctorId == doctorId);
        }
        else if (slot.RoomId.HasValue)
        {
            var roomId = slot.RoomId.Value;
            query = query.Where(a => a.Slot!.RoomId == roomId);
        }
        else
        {
            var slotId = slot.Id;
            query = query.Where(a => a.SlotId == slotId);
        }

        var rank = await query.CountAsync(ct);
        return rank.ToString("D3", CultureInfo.InvariantCulture);
    }
}
