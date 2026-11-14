using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class PaymentsWebhookController : ControllerBase
{
    private readonly ISePayWebhookService _service;
    private readonly IConfiguration _configuration;

    public PaymentsWebhookController(ISePayWebhookService service, IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    /// <summary>POST /api/v1/payments/sepay/webhook — SePay báo giao dịch vào, xác nhận PAID.</summary>
    [HttpPost("/api/v1/payments/sepay/webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SePay(
        [FromBody] SePayWebhookRequest request,
        CancellationToken ct)
    {
        if (!HasValidApiKey())
            return Unauthorized();

        await _service.HandleAsync(request, ct);
        return Ok(new { success = true });
    }

    private bool HasValidApiKey()
    {
        var expected = _configuration["SePay:WebhookApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return false;

        var parts = header.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("Apikey", StringComparison.OrdinalIgnoreCase))
            return false;

        var provided = Encoding.UTF8.GetBytes(parts[1].Trim());
        var configured = Encoding.UTF8.GetBytes(expected.Trim());
        return CryptographicOperations.FixedTimeEquals(provided, configured);
    }
}
