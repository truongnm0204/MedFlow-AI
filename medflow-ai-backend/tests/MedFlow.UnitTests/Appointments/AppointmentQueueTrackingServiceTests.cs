using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;

namespace MedFlow.UnitTests.Appointments;

public sealed class AppointmentQueueTrackingServiceTests : IDisposable
{
    private readonly AppointmentReadTestDb.Context _db;

    public AppointmentQueueTrackingServiceTests()
    {
        _db = AppointmentReadTestDb.CreateSeeded();
    }

    public void Dispose() => _db.Dispose();

    private AppointmentQueueTrackingService CreateSut(TimeProvider? timeProvider = null)
        => new(
            _db,
            timeProvider ?? new AppointmentReadTestDb.FixedTimeProvider(AppointmentReadTestDb.DefaultUtcNow));

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_ConfirmedUpcoming_ReturnsAwaitingCheckInWithSteps()
    {
        var sut = CreateSut();

        var dto = await sut.GetMyAppointmentQueueStatusAsync(1, "103");

        Assert.Equal("103", dto.AppointmentId);
        Assert.Equal("APT-202611-0103", dto.AppointmentCode);
        Assert.Equal("VIS-202611-0103", dto.VisitCode);
        Assert.Equal("001", dto.QueueNumber);
        Assert.Equal("AWAITING_CHECK_IN", dto.Status);
        Assert.Equal("CHỜ CHECK-IN", dto.StatusLabel);
        Assert.Equal("Khám Tiêu hóa 101", dto.RoomName);
        Assert.Contains("Nguyễn Minh Đức", dto.DoctorName);
        Assert.Equal("Khoa Tiêu hóa", dto.DepartmentName);
        Assert.Null(dto.ServingQueueNumber);
        Assert.Null(dto.NextQueueNumber);
        Assert.Null(dto.EstimatedWaitLabel);

        Assert.Equal(
            ["CHECK_IN", "CONSULTATION", "COMPLETE"],
            dto.VisitSteps.Select(s => s.Key).ToArray());
        Assert.Equal(
            ["current", "upcoming", "upcoming"],
            dto.VisitSteps.Select(s => s.State).ToArray());
        Assert.Equal("CHƯA CHECK-IN", dto.VisitSteps[0].StatusLabel);
        Assert.Contains("13:45", dto.VisitSteps[0].Note);
        Assert.Null(dto.VisitSteps[1].StatusLabel);
        Assert.Null(dto.VisitSteps[2].StatusLabel);
        Assert.Contains("001", dto.GuidanceNote);
    }

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_VerifiedChildMember_ReturnsStoredQueueNumber()
    {
        var sut = CreateSut();

        var dto = await sut.GetMyAppointmentQueueStatusAsync(1, "  #apt-202611-0102 ");

        Assert.Equal("102", dto.AppointmentId);
        Assert.Equal("APT-202611-0102", dto.AppointmentCode);
        Assert.Equal("VIS-202611-0102", dto.VisitCode);
        Assert.Equal("007", dto.QueueNumber);
        Assert.Equal("AWAITING_CHECK_IN", dto.Status);
        Assert.Equal("CHỜ CHECK-IN", dto.StatusLabel);
    }

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_CheckedInWithLiveBucket_EvaluatesServingNextAndEstimatedWait()
    {
        var queueDate = new DateOnly(2026, 11, 4);
        var appointment = _db.Appointments.Single(a => a.Id == 103);
        appointment.Status = "CHECKED_IN";

        _db.Appointments.AddRange(
            new Appointment
            {
                Id = 201,
                AppointmentCode = "APT-202611-0201",
                PatientId = 2,
                SlotId = 12,
                Status = "CHECKED_IN",
            },
            new Appointment
            {
                Id = 202,
                AppointmentCode = "APT-202611-0202",
                PatientId = 3,
                SlotId = 12,
                Status = "CHECKED_IN",
            });

        _db.Visits.AddRange(
            new Visit
            {
                Id = 511,
                AppointmentId = 201,
                PatientId = 2,
                ExaminationRoomId = 1,
                AssignedDoctorId = 1,
                Status = "IN_PROGRESS",
                CheckedInAt = new DateTime(2026, 11, 4, 13, 30, 0),
            },
            new Visit
            {
                Id = 512,
                AppointmentId = 202,
                PatientId = 3,
                ExaminationRoomId = 1,
                AssignedDoctorId = 1,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 11, 4, 13, 46, 0),
            },
            new Visit
            {
                Id = 513,
                VisitCode = "VIS-202611-0513",
                AppointmentId = 103,
                PatientId = 1,
                ExaminationRoomId = 1,
                AssignedDoctorId = 1,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 11, 4, 13, 50, 0),
            });

        _db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 611,
                VisitId = 511,
                AppointmentId = 201,
                QueueDate = queueDate,
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "IN_PROGRESS",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 11, 4, 13, 30, 0),
                StartedAt = new DateTime(2026, 11, 4, 13, 55, 0),
            },
            new QueueEntry
            {
                Id = 612,
                VisitId = 512,
                AppointmentId = 202,
                QueueDate = queueDate,
                QueueNumber = 2,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 11, 4, 13, 46, 0),
            },
            new QueueEntry
            {
                Id = 613,
                VisitId = 513,
                AppointmentId = 103,
                QueueDate = queueDate,
                QueueNumber = 3,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 11, 4, 13, 50, 0),
            });

        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var dto = await sut.GetMyAppointmentQueueStatusAsync(1, "103");

        Assert.Equal("VIS-202611-0513", dto.VisitCode);
        Assert.Equal("003", dto.QueueNumber);
        Assert.Equal("WAITING", dto.Status);
        Assert.Equal("ĐANG CHỜ KHÁM", dto.StatusLabel);
        Assert.Equal("001", dto.ServingQueueNumber);
        Assert.Equal("002", dto.NextQueueNumber);
        Assert.Equal("Ước tính chờ ~30 phút", dto.EstimatedWaitLabel);
        Assert.Equal(
            ["done", "current", "upcoming"],
            dto.VisitSteps.Select(s => s.State).ToArray());
        Assert.Equal("Đã check-in", dto.VisitSteps[0].StatusLabel);
        Assert.Equal("CHƯA BẮT ĐẦU", dto.VisitSteps[1].StatusLabel);
    }

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_ProgressesThroughCalledInProgressSkippedAwaitingDiagnosticsAndCompleted()
    {
        var queueDate = new DateOnly(2026, 11, 4);
        var appointment = _db.Appointments.Single(a => a.Id == 103);
        appointment.Status = "CHECKED_IN";

        var visit = new Visit
        {
            Id = 520,
            AppointmentId = 103,
            PatientId = 1,
            ExaminationRoomId = 1,
            AssignedDoctorId = 1,
            Status = "WAITING",
            CheckedInAt = new DateTime(2026, 11, 4, 13, 45, 0),
        };
        var queue = new QueueEntry
        {
            Id = 620,
            VisitId = 520,
            AppointmentId = 103,
            QueueDate = queueDate,
            QueueNumber = 5,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            ArrivedAt = new DateTime(2026, 11, 4, 13, 45, 0),
        };
        _db.Visits.Add(visit);
        _db.QueueEntries.Add(queue);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // 1. First in empty room -> Chuẩn bị đến lượt khám
        var readyNext = await sut.GetMyAppointmentQueueStatusAsync(1, "103");
        Assert.Equal("WAITING", readyNext.Status);
        Assert.Null(readyNext.ServingQueueNumber);
        Assert.Equal("005", readyNext.NextQueueNumber);
        Assert.Equal("Chuẩn bị đến lượt khám", readyNext.EstimatedWaitLabel);

        // 2. CALLED
        queue.Status = "CALLED";
        queue.CalledAt = new DateTime(2026, 11, 4, 14, 0, 0);
        await _db.SaveChangesAsync();

        var called = await sut.GetMyAppointmentQueueStatusAsync(1, "103");
        Assert.Equal("WAITING", called.Status);
        Assert.Equal("ĐÃ GỌI SỐ - MỜI VÀO PHÒNG", called.StatusLabel);
        Assert.Equal("005", called.ServingQueueNumber);
        Assert.Equal("Đã đến lượt vào khám", called.EstimatedWaitLabel);
        Assert.Equal("ĐÃ GỌI SỐ", called.VisitSteps[1].StatusLabel);

        // 3. IN_PROGRESS
        queue.Status = "IN_PROGRESS";
        queue.StartedAt = new DateTime(2026, 11, 4, 14, 3, 0);
        visit.Status = "IN_PROGRESS";
        await _db.SaveChangesAsync();

        var inProgress = await sut.GetMyAppointmentQueueStatusAsync(1, "103");
        Assert.Equal("WAITING", inProgress.Status);
        Assert.Equal("ĐANG KHÁM", inProgress.StatusLabel);
        Assert.Equal("005", inProgress.ServingQueueNumber);
        Assert.Equal("Đang trong phòng khám", inProgress.EstimatedWaitLabel);
        Assert.Equal("ĐANG KHÁM", inProgress.VisitSteps[1].StatusLabel);

        // 4. Open DIAGNOSTIC (Awaiting results)
        queue.Status = "WAITING";
        queue.StartedAt = null;
        queue.CalledAt = null;
        queue.QueueType = "RETURN_CONSULTATION";
        visit.Status = "AWAITING_RESULTS";
        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 621,
            VisitId = 520,
            QueueDate = queueDate,
            QueueType = "DIAGNOSTIC",
            Status = "WAITING",
        });
        await _db.SaveChangesAsync();

        var awaitingCls = await sut.GetMyAppointmentQueueStatusAsync(1, "103");
        Assert.Equal("WAITING", awaitingCls.Status);
        Assert.Equal("ĐANG CHỜ KHÁM", awaitingCls.StatusLabel);
        Assert.Equal("Đang chờ kết quả cận lâm sàng", awaitingCls.EstimatedWaitLabel);
        Assert.Equal("CHỜ KẾT QUẢ CLS", awaitingCls.VisitSteps[1].StatusLabel);

        // 5. SKIPPED
        queue.Status = "SKIPPED";
        queue.PresenceStatus = "LEFT";
        visit.Status = "WAITING";
        await _db.SaveChangesAsync();

        var skipped = await sut.GetMyAppointmentQueueStatusAsync(1, "103");
        Assert.Equal("WAITING", skipped.Status);
        Assert.Equal("TẠM QUA LƯỢT", skipped.StatusLabel);
        Assert.Equal("Tạm qua lượt — vui lòng báo quầy tiếp đón", skipped.EstimatedWaitLabel);
        Assert.Equal("TẠM QUA LƯỢT", skipped.VisitSteps[1].StatusLabel);

        // 6. COMPLETED (also check seeded 101)
        var completed = await sut.GetMyAppointmentQueueStatusAsync(1, "101");
        Assert.Equal("COMPLETED", completed.Status);
        Assert.Equal("ĐÃ HOÀN THÀNH", completed.StatusLabel);
        Assert.Null(completed.EstimatedWaitLabel);
        Assert.All(completed.VisitSteps, s => Assert.Equal("done", s.State));
    }

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_VisitRoomAndDoctorOverride_ReflectsTransferredAssignment()
    {
        var newDept = new Department
        {
            Id = 3,
            HospitalId = 1,
            DepartmentCode = "CARD",
            Name = "Khoa Tim mạch",
            IsActive = true,
        };
        var newRoom = new HospitalRoom
        {
            Id = 2,
            HospitalId = 1,
            DepartmentId = 3,
            RoomCode = "RM-202",
            Name = "Phòng Tim mạch 202",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
            Department = newDept,
        };
        var newStaff = new Staff
        {
            Id = 2,
            UserId = 4,
            HospitalId = 1,
            DepartmentId = 3,
            StaffCode = "STF-002",
            FullName = "Trần Hoàng Nam",
            Status = "ACTIVE",
        };
        var newDoctor = new Doctor
        {
            Id = 2,
            StaffId = 2,
            HospitalId = 1,
            SpecialtyId = 1,
            DoctorCode = "DOC-002",
            ProfessionalTitle = "ThS.BS",
            IsActive = true,
            Staff = newStaff,
        };

        _db.Departments.Add(newDept);
        _db.HospitalRooms.Add(newRoom);
        _db.Staff.Add(newStaff);
        _db.Doctors.Add(newDoctor);

        var appointment = _db.Appointments.Single(a => a.Id == 103);
        appointment.Status = "CHECKED_IN";
        _db.Visits.Add(new Visit
        {
            Id = 530,
            AppointmentId = 103,
            PatientId = 1,
            ExaminationRoomId = 2,
            AssignedDoctorId = 2,
            Status = "WAITING",
            CheckedInAt = new DateTime(2026, 11, 4, 13, 50, 0),
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var dto = await sut.GetMyAppointmentQueueStatusAsync(1, "103");

        Assert.Equal("Phòng Tim mạch 202", dto.RoomName);
        Assert.Equal("Khoa Tim mạch", dto.DepartmentName);
        Assert.Equal("ThS.BS Trần Hoàng Nam", dto.DoctorName);
        Assert.Contains("Phòng Tim mạch 202", dto.GuidanceNote);
    }

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_CancelledAppointment_ThrowsConflictExceptionWithExactMessage()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.GetMyAppointmentQueueStatusAsync(1, "104"));

        Assert.Equal("Lịch hẹn đã hủy nên không còn số thứ tự trong hàng đợi.", ex.Message);
    }

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_NoShowAppointment_ThrowsConflictException()
    {
        var appointment = _db.Appointments.Single(a => a.Id == 103);
        appointment.Status = "NO_SHOW";
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.GetMyAppointmentQueueStatusAsync(1, "103"));

        Assert.Equal("Lịch hẹn đã ghi nhận vắng mặt nên không còn số thứ tự trong hàng đợi.", ex.Message);
    }

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_CancelledQueueEntry_ThrowsConflictException()
    {
        var queue = _db.QueueEntries.Single(q => q.Id == 601);
        queue.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.GetMyAppointmentQueueStatusAsync(1, "102"));

        Assert.Equal("Số thứ tự trong hàng đợi của lượt khám này đã bị hủy.", ex.Message);
    }

    [Theory]
    [InlineData("APT-REVOKED-0105")]
    [InlineData("APT-OTHER-0106")]
    public async Task GetMyAppointmentQueueStatusAsync_OutOfScope_ThrowsForbiddenException(string idOrCode)
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetMyAppointmentQueueStatusAsync(1, idOrCode));

        Assert.Equal("Bạn không có quyền xem hàng đợi của lịch hẹn này.", ex.Message);
    }

    [Theory]
    [InlineData("99999999")]
    [InlineData("APT-NOPE")]
    public async Task GetMyAppointmentQueueStatusAsync_NonExistent_ThrowsNotFoundException(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetMyAppointmentQueueStatusAsync(1, idOrCode));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("   ")]
    public async Task GetMyAppointmentQueueStatusAsync_InvalidIdentifier_ThrowsValidationException(string idOrCode)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetMyAppointmentQueueStatusAsync(1, idOrCode));
    }

    [Fact]
    public async Task GetMyAppointmentQueueStatusAsync_InvalidUserId_ThrowsUnauthorizedAccessException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.GetMyAppointmentQueueStatusAsync(0, "103"));
    }
}
