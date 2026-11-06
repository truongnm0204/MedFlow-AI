namespace MedFlow.Application.DTOs.Policies;

/// <summary>
/// Tỷ lệ điều phối xen kẽ giữa nhóm ưu tiên và nhóm thường đối với bệnh nhân vãng lai.
/// </summary>
/// <param name="Priority">Trọng số số lượt gọi liên tiếp cho nhóm ưu tiên.</param>
/// <param name="Normal">Trọng số số lượt gọi xen kẽ cho nhóm thường.</param>
public sealed record QueuePriorityRatioDto(
    int Priority,
    int Normal);
