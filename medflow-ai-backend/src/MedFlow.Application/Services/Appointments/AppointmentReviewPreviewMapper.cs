using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Services.Appointments;

internal static class AppointmentReviewPreviewMapper
{
    internal const int EarlyArrivalMinutes = 15;

    public static AppointmentReviewPreviewResponse Map(
        Patient patient,
        string? relationship,
        bool isSelf,
        Patient accountOwner,
        string? symptomNote,
        bool isDoctorMode,
        bool isMorningShift,
        ExaminationServiceDto service,
        DoctorDetailDto? doctor,
        ExaminationSlot slot,
        HospitalRoom? room,
        AppointmentPolicyDto policy,
        DateOnly today)
    {
        var start = slot.StartsAt!.Value;
        var end = slot.EndsAt!.Value;

        var patientInfo = BuildPatientInfo(
            patient,
            relationship,
            isSelf,
            accountOwner,
            symptomNote,
            today);

        var checkInDeadlineTime = start
            .AddMinutes(-EarlyArrivalMinutes)
            .ToString("HH:mm", CultureInfo.InvariantCulture);

        var allocatedRoomTitle = ResolveAllocatedRoomTitle(room, doctor, service);
        var shortRoomNumber = ResolveShortRoomNumber(room, allocatedRoomTitle);

        var scheduleLocation = BuildScheduleLocation(
            isDoctorMode,
            isMorningShift,
            service,
            doctor,
            slot,
            start,
            end,
            checkInDeadlineTime,
            allocatedRoomTitle,
            shortRoomNumber);

        var feeSummary = BuildFeeSummary(service, doctor);

        var receptionPolicy = new ReviewReceptionPolicyDto(
            BadgeLabel: "Chính xác theo phút",
            RoomNumber: shortRoomNumber,
            ReceptionCounterLabel: "Quầy Tiếp đón Ngoại trú (Tầng 1 - Nhà A)",
            EarlyArrivalMinutes: EarlyArrivalMinutes,
            CheckInDeadlineTime: checkInDeadlineTime,
            LateGraceMinutes: policy.NoShowGraceMinutes);

        return new AppointmentReviewPreviewResponse(
            PatientInfo: patientInfo,
            ScheduleLocation: scheduleLocation,
            FeeSummary: feeSummary,
            ReceptionPolicy: receptionPolicy);
    }

    private static ReviewPatientInfoDto BuildPatientInfo(
        Patient patient,
        string? relationship,
        bool isSelf,
        Patient accountOwner,
        string? symptomNote,
        DateOnly today)
    {
        var relationshipBadgeLabel = isSelf
            ? "Chính chủ tài khoản"
            : $"Hồ sơ người thân • {AppointmentSlotService.FormatRelationshipLabel(relationship).Trim('(', ')')}";

        var dateOfBirth = patient.DateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            ?? "Chưa cập nhật";

        var ageLabel = patient.DateOfBirth.HasValue
            ? $"({AppointmentSlotService.CalculateAge(patient.DateOfBirth, today).ToString(CultureInfo.InvariantCulture)} tuổi)"
            : string.Empty;

        var phone = !string.IsNullOrWhiteSpace(patient.Phone)
            ? patient.Phone.Trim()
            : !string.IsNullOrWhiteSpace(accountOwner.Phone)
                ? accountOwner.Phone.Trim()
                : "Chưa cập nhật";

        var nationalId = !string.IsNullOrWhiteSpace(patient.IdentificationHash)
            ? PatientProfileService.MaskedVerifiedIdentityText
            : "Chưa xác thực CCCD";

        var currentAddress = !string.IsNullOrWhiteSpace(patient.Address)
            ? patient.Address.Trim()
            : "Chưa cập nhật";

        return new ReviewPatientInfoDto(
            PatientId: patient.Id.ToString(CultureInfo.InvariantCulture),
            PatientCode: patient.PatientCode,
            FullName: patient.FullName,
            RelationshipBadgeLabel: relationshipBadgeLabel,
            IsPrimaryAccount: isSelf,
            DateOfBirth: dateOfBirth,
            AgeLabel: ageLabel,
            Gender: AppointmentSlotService.FormatGender(patient.Gender),
            Phone: phone,
            NationalId: nationalId,
            CurrentAddress: currentAddress,
            SymptomNote: symptomNote ?? string.Empty);
    }

    private static ReviewScheduleLocationDto BuildScheduleLocation(
        bool isDoctorMode,
        bool isMorningShift,
        ExaminationServiceDto service,
        DoctorDetailDto? doctor,
        ExaminationSlot slot,
        DateTime start,
        DateTime end,
        string checkInDeadlineTime,
        string allocatedRoomTitle,
        string shortRoomNumber)
    {
        var mode = isDoctorMode ? "doctor" : "service";
        var doctorId = doctor?.Id ?? string.Empty;
        var doctorName = !string.IsNullOrWhiteSpace(doctor?.Name)
            ? doctor.Name
            : "Bác sĩ điều phối theo ca khám";
        var doctorAvatarUrl = doctor?.AvatarUrl ?? string.Empty;

        string doctorRoleBadge;
        if (!isDoctorMode)
        {
            doctorRoleBadge = "BÁC SĨ PHỤ TRÁCH CA TRỰC CHUYÊN KHOA";
        }
        else if (!string.IsNullOrWhiteSpace(doctor?.Title))
        {
            doctorRoleBadge = $"{doctor.Title.Trim().ToUpperInvariant()} - BÁC SĨ ĐIỀU TRỊ CHUYÊN KHOA";
        }
        else
        {
            doctorRoleBadge = "BÁC SĨ ĐIỀU TRỊ CHUYÊN KHOA";
        }

        string doctorCredentialsSubtitle;
        if (isDoctorMode && doctor is not null)
        {
            doctorCredentialsSubtitle = $"{doctor.Specialty} • {doctor.YearsOfExperience.ToString(CultureInfo.InvariantCulture)} năm kinh nghiệm";
        }
        else if (doctor is not null)
        {
            doctorCredentialsSubtitle = $"{doctor.YearsOfExperience.ToString(CultureInfo.InvariantCulture)} năm kinh nghiệm • Điều phối tự động theo ca khám";
        }
        else
        {
            doctorCredentialsSubtitle = "Điều phối tự động theo ca khám";
        }

        var dateIso = DateOnly.FromDateTime(start).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var slotOrShiftId = isDoctorMode
            ? slot.Id.ToString(CultureInfo.InvariantCulture)
            : (isMorningShift ? "shift-morning" : "shift-afternoon");

        var shiftLabel = start.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart
            ? "Ca Sáng"
            : "Ca Chiều";

        var scheduledSlotDisplay = string.Format(
            CultureInfo.InvariantCulture,
            "{0:HH:mm} - {1:HH:mm}, {2}, ngày {0:dd/MM/yyyy} ({3})",
            start,
            end,
            AppointmentSlotService.FormatFullWeekday(start.DayOfWeek),
            shiftLabel);

        var durationMinutesText = service.DurationMinutes.ToString(CultureInfo.InvariantCulture);

        return new ReviewScheduleLocationDto(
            Mode: mode,
            DoctorId: doctorId,
            DoctorName: doctorName,
            DoctorRoleBadge: doctorRoleBadge,
            DoctorCredentialsSubtitle: doctorCredentialsSubtitle,
            DoctorAvatarUrl: doctorAvatarUrl,
            SpecialtyName: service.DepartmentName,
            ServiceId: service.Id,
            ServiceCode: service.ServiceCode,
            ServiceName: service.Name,
            ServiceDisplayLabel: $"{service.Name} (Mã {service.ServiceCode})",
            DateIso: dateIso,
            SlotOrShiftId: slotOrShiftId,
            ScheduledSlotDisplay: scheduledSlotDisplay,
            CheckInDeadlineTime: checkInDeadlineTime,
            AllocatedRoomTitle: allocatedRoomTitle,
            ShortRoomNumber: shortRoomNumber,
            DurationBadgeLabel: $"{durationMinutesText} phút/lượt khám",
            DurationFootnote: $"Thời lượng chuẩn: {durationMinutesText} phút thăm khám chuyên sâu trực tiếp kèm tư vấn phác đồ điều trị.");
    }

    private static string ResolveAllocatedRoomTitle(
        HospitalRoom? room,
        DoctorDetailDto? doctor,
        ExaminationServiceDto service)
    {
        if (room is not null)
            return DoctorCatalogService.FormatHospitalRoom(room);

        if (!string.IsNullOrWhiteSpace(doctor?.Room))
            return doctor.Room.Trim();

        if (!string.IsNullOrWhiteSpace(service.Location))
            return service.Location.Trim();

        return "Phòng khám sẽ được thông báo tại Quầy Tiếp đón";
    }

    private static string ResolveShortRoomNumber(HospitalRoom? room, string allocatedRoomTitle)
    {
        if (room is not null)
            return DoctorCatalogService.ExtractRoomLabel(room);

        const string prefix = "Phòng ";
        if (allocatedRoomTitle.StartsWith(prefix, StringComparison.Ordinal))
        {
            var afterPrefix = allocatedRoomTitle[prefix.Length..];
            var separatorIndex = afterPrefix.IndexOf(" - ", StringComparison.Ordinal);
            var label = separatorIndex >= 0 ? afterPrefix[..separatorIndex] : afterPrefix;
            return label.Trim();
        }

        return string.Empty;
    }

    private static ReviewFeeSummaryDto BuildFeeSummary(
        ExaminationServiceDto service,
        DoctorDetailDto? doctor)
    {
        var price = service.Price > 0 ? service.Price : doctor?.Price ?? 0m;
        var mainTitle = !string.IsNullOrWhiteSpace(doctor?.Title)
            ? $"{service.Name} ({doctor.Title.Trim()})"
            : service.Name;

        IReadOnlyList<ReviewFeeLineItemDto> items =
        [
            new ReviewFeeLineItemDto(
                Id: "fee-main-consultation",
                Title: mainTitle,
                Description: "Khám lâm sàng chuyên sâu, chẩn đoán ban đầu & tư vấn",
                Amount: price,
                IsIncludedFree: null,
                FreeBadgeLabel: null),
            new ReviewFeeLineItemDto(
                Id: "fee-vitals-ehr",
                Title: "Đo sinh hiệu ban đầu & Lập hồ sơ y tế số",
                Description: "Đo huyết áp, nhịp tim, SpO2, lập mã QR định danh hồ sơ",
                Amount: 0m,
                IsIncludedFree: true,
                FreeBadgeLabel: "0 VNĐ (Đã bao gồm trọn gói)")
        ];

        return new ReviewFeeSummaryDto(
            ServiceTypeBadge: "Dịch vụ Ngoại trú Tự nguyện",
            Items: items,
            TotalAmount: price,
            TotalDisclaimer: "Chưa bao gồm xét nghiệm hoặc chẩn đoán hình ảnh chuyên sâu (nếu có chỉ định từ bác sĩ)",
            PaymentMethodNote: "Quý khách có thể quét mã VietQR động bảo mật ngay sau khi xác nhận trên hệ thống hoặc thanh toán trực tiếp bằng tiền mặt / quẹt thẻ POS tại Quầy Tiếp đón khi đến viện.");
    }
}
