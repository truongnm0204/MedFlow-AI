using MedFlow.Application.Services.Diagnostic;
using MedFlow.Domain.Entities;

namespace MedFlow.UnitTests.Diagnostic;

/// <summary>
/// #869 — quy tắc đề xuất thứ tự CLS và chọn điểm theo tải (owner chốt 29/11).
/// </summary>
public sealed class DiagnosticRoutingRulesTests
{
    private sealed record Item(long ServiceId, Service Service);

    private static Item MakeItem(
        long id,
        string? group = null,
        bool fasting = false,
        string? code = null,
        string serviceType = "DIAGNOSTIC",
        long departmentId = 1)
        => new(
            id,
            new Service
            {
                Id = id,
                ServiceCode = code ?? $"SVC-{id}",
                Name = $"Service {id}",
                ServiceType = serviceType,
                IsActive = true,
                DepartmentId = departmentId,
                DiagnosticGroup = group,
                RequiresFasting = fasting,
            });

    private static IReadOnlyList<long> OrderIds(
        IReadOnlyList<Item> items,
        IReadOnlyDictionary<long, HashSet<long>>? dependencies = null,
        IReadOnlyDictionary<long, int>? loads = null)
        => DiagnosticRoutingRules.OrderForSequencing(
                items,
                item => item.ServiceId,
                item => item.Service,
                item => loads is not null && loads.TryGetValue(item.ServiceId, out var load) ? load : 0,
                dependencies ?? new Dictionary<long, HashSet<long>>())
            .Select(item => item.ServiceId)
            .ToList();

    [Fact]
    public void FastingFirst_ThenBloodTest_Imaging_Functional()
    {
        var items = new[]
        {
            MakeItem(1, DiagnosticRoutingRules.GroupFunctional),
            MakeItem(2, DiagnosticRoutingRules.GroupImaging),
            MakeItem(3, DiagnosticRoutingRules.GroupBloodTest),
            MakeItem(4, DiagnosticRoutingRules.GroupImaging, fasting: true),
        };

        var ordered = OrderIds(items);

        Assert.Equal([4, 3, 2, 1], ordered);
    }

    [Fact]
    public void SameGroup_LowerWaitingLoadFirst()
    {
        var items = new[]
        {
            MakeItem(10, DiagnosticRoutingRules.GroupImaging),
            MakeItem(11, DiagnosticRoutingRules.GroupImaging),
            MakeItem(12, DiagnosticRoutingRules.GroupImaging),
        };
        var loads = new Dictionary<long, int> { [10] = 5, [11] = 1, [12] = 3 };

        var ordered = OrderIds(items, loads: loads);

        Assert.Equal([11, 12, 10], ordered);
    }

    [Fact]
    public void Dependency_OverridesGroupRanking()
    {
        // FUNCTIONAL (rank 2) phải sau IMAGING, nhưng cấu hình yêu cầu IMAGING phụ thuộc FUNCTIONAL
        // => điều kiện chuyên môn thắng thứ hạng nhóm.
        var items = new[]
        {
            MakeItem(20, DiagnosticRoutingRules.GroupFunctional),
            MakeItem(21, DiagnosticRoutingRules.GroupImaging),
        };
        var dependencies = new Dictionary<long, HashSet<long>>
        {
            [21] = [20],
        };

        var ordered = OrderIds(items, dependencies);

        Assert.Equal([20, 21], ordered);
    }

    [Fact]
    public void DependencyCycle_DoesNotHang_AndFallsBackToRanking()
    {
        var items = new[]
        {
            MakeItem(30, DiagnosticRoutingRules.GroupBloodTest),
            MakeItem(31, DiagnosticRoutingRules.GroupImaging),
        };
        var dependencies = new Dictionary<long, HashSet<long>>
        {
            [30] = [31],
            [31] = [30],
        };

        var ordered = OrderIds(items, dependencies);

        Assert.Equal([30, 31], ordered);
    }

    [Fact]
    public void ResolveForAssignment_PicksLeastLoadedCompatibleActivePoint()
    {
        const long hospitalId = 1;
        const long deptId = 10;
        var service = new Service
        {
            Id = 100,
            ServiceCode = "LAB-CBC",
            Name = "Công thức máu",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
            DepartmentId = deptId,
        };
        var departments = new Dictionary<long, Department>
        {
            [deptId] = new Department { Id = deptId, HospitalId = hospitalId, IsActive = true },
        };
        var rooms = new Dictionary<long, HospitalRoom>
        {
            [1001] = new HospitalRoom { Id = 1001, HospitalId = hospitalId, DepartmentId = deptId, RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
            [1002] = new HospitalRoom { Id = 1002, HospitalId = hospitalId, DepartmentId = deptId, RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
            [1003] = new HospitalRoom { Id = 1003, HospitalId = hospitalId, DepartmentId = deptId, RoomType = "DIAGNOSTIC", Status = "MAINTENANCE" },
        };
        var points = new List<QueueServicePoint>
        {
            new() { Id = 1, HospitalId = hospitalId, RoomId = 1001, PointType = "DIAGNOSTIC", Name = "P1", IsActive = true },
            new() { Id = 2, HospitalId = hospitalId, RoomId = 1002, PointType = "DIAGNOSTIC", Name = "P2", IsActive = true },
            new() { Id = 3, HospitalId = hospitalId, RoomId = 1003, PointType = "DIAGNOSTIC", Name = "P3 (bảo trì)", IsActive = true },
            new() { Id = 4, HospitalId = hospitalId, RoomId = 1001, PointType = "DIAGNOSTIC", Name = "P4 (đóng)", IsActive = false },
        };
        var loads = new Dictionary<long, int> { [1] = 4, [2] = 1, [3] = 0 };

        var selected = DiagnosticServicePointResolver.ResolveForAssignment(
            service, points, rooms, departments, hospitalId, loads);

        Assert.NotNull(selected);
        Assert.Equal(2, selected!.Id);
    }

    [Fact]
    public void ResolveForAssignment_ReturnsNull_WhenNoOperationalCompatiblePoint()
    {
        const long hospitalId = 1;
        const long deptId = 10;
        const long otherDeptId = 20;
        var service = new Service
        {
            Id = 100,
            ServiceCode = "LAB-CBC",
            Name = "Công thức máu",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
            DepartmentId = deptId,
        };
        var departments = new Dictionary<long, Department>
        {
            [deptId] = new Department { Id = deptId, HospitalId = hospitalId, IsActive = true },
            [otherDeptId] = new Department { Id = otherDeptId, HospitalId = hospitalId, IsActive = true },
        };
        var rooms = new Dictionary<long, HospitalRoom>
        {
            [1001] = new HospitalRoom { Id = 1001, HospitalId = hospitalId, DepartmentId = otherDeptId, RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
        };
        var points = new List<QueueServicePoint>
        {
            new() { Id = 1, HospitalId = hospitalId, RoomId = 1001, PointType = "DIAGNOSTIC", Name = "P1", IsActive = true },
        };

        var selected = DiagnosticServicePointResolver.ResolveForAssignment(
            service, points, rooms, departments, hospitalId, new Dictionary<long, int>());

        Assert.Null(selected);
    }

    [Fact]
    public void ResolveForExecution_PrefersAssignedPoint_WhenStillOperational()
    {
        const long hospitalId = 1;
        const long deptId = 10;
        var service = new Service
        {
            Id = 100,
            ServiceCode = "LAB-CBC",
            Name = "Công thức máu",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
            DepartmentId = deptId,
        };
        var departments = new Dictionary<long, Department>
        {
            [deptId] = new Department { Id = deptId, HospitalId = hospitalId, IsActive = true },
        };
        var rooms = new Dictionary<long, HospitalRoom>
        {
            [1001] = new HospitalRoom { Id = 1001, HospitalId = hospitalId, DepartmentId = deptId, RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
            [1002] = new HospitalRoom { Id = 1002, HospitalId = hospitalId, DepartmentId = deptId, RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
        };
        var points = new List<QueueServicePoint>
        {
            new() { Id = 1, HospitalId = hospitalId, RoomId = 1001, PointType = "DIAGNOSTIC", Name = "P1", IsActive = true },
            new() { Id = 2, HospitalId = hospitalId, RoomId = 1002, PointType = "DIAGNOSTIC", Name = "P2", IsActive = true },
        };
        var order = new DiagnosticOrder
        {
            Id = 9001,
            OrderCode = "DIA-9001",
            ServiceId = 100,
            Status = "CONFIRMED",
            AssignedServicePointId = 2,
        };

        var resolved = DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
            order, service, requestedSpId: null, points, rooms, visitQueues: null, hospitalId, departments);

        Assert.Equal(2, resolved);
    }
}
