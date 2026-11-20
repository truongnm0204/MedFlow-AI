namespace MedFlow.Application.Options;

public sealed class IdentityHashOptions
{
    /// <summary>Key HMAC cho số định danh. Không commit; lấy từ secret/env `Identity:HashKey`.</summary>
    public string? HashKey { get; set; }

    /// <summary>`dual` = đọc HMAC rồi fallback SHA; `hmac` = chỉ HMAC.</summary>
    public string Mode { get; set; } = "dual";
}
