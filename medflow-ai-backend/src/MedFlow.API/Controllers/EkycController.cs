using System.Security.Claims;
using MedFlow.Application.DTOs.Ekyc;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Ekyc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MedFlow.API.Controllers;

/// <summary>
/// eKYC endpoints: session lifecycle + transparent VNPT proxy.
/// PM ruling: transparent proxy + session correlation via path + strict finalize — no per-step
/// ordering enforcement at the proxy layer (scan3d payloads are opaque; trust anchor is finalize).
/// </summary>
[ApiController]
[Route("api/v1/ekyc")]
public sealed class EkycController : ControllerBase
{
    private readonly IEkycSessionService _sessionService;
    private readonly IEkycProxyService _proxyService;

    public EkycController(IEkycSessionService sessionService, IEkycProxyService proxyService)
    {
        _sessionService = sessionService;
        _proxyService = proxyService;
    }

    /// <summary>POST /api/v1/ekyc/sessions — create consent-gated eKYC session.</summary>
    [HttpPost("sessions")]
    [Authorize]
    [EnableRateLimiting("ekyc-session-create")]
    [ProducesResponseType<CreateEkycSessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CreateEkycSessionResponse>> CreateSession(
        [FromBody] CreateEkycSessionRequest request,
        CancellationToken ct)
    {
        var response = await _sessionService.CreateAsync(
            GetUserId(), request, GetIp(), GetUserAgent(), ct);
        return CreatedAtAction(nameof(GetSession), new { id = response.SessionId }, response);
    }

    /// <summary>GET /api/v1/ekyc/sessions/{id} — session status (ownership-enforced, no evidence leak).</summary>
    [HttpGet("sessions/{id:long}")]
    [Authorize]
    [ProducesResponseType<EkycSessionStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EkycSessionStatusResponse>> GetSession(
        long id,
        CancellationToken ct)
        => Ok(await _sessionService.GetStatusAsync(GetUserId(), id, ct));

    /// <summary>
    /// ANY /api/v1/ekyc/proxy/{sessionId}/{**path} — transparent forward to VNPT.
    /// The body is read into bounded memory (never persisted, never logged) and forwarded verbatim;
    /// the response is returned verbatim with its original status code and content type.
    /// </summary>
    [AcceptVerbs("GET", "POST", "HEAD")]
    [Route("proxy/{sessionId:long}/{**path}")]
    [Authorize]
    [EnableRateLimiting("ekyc-proxy")]
    public async Task<IActionResult> Proxy(
        long sessionId,
        string path,
        CancellationToken ct)
    {
        var requestBody = await ReadBoundedBodyAsync(ct);
        var contentType = Request.ContentType;

        var result = await _proxyService.ForwardAsync(
            GetUserId(),
            sessionId,
            path ?? string.Empty,
            Request.Method,
            contentType,
            requestBody,
            ct);

        foreach (var header in result.Headers)
        {
            // Response headers whitelist (content-type only) — never forward upstream auth headers.
            Response.Headers[header.Key] = header.Value;
        }

        // Write the upstream response verbatim: original status code, original body bytes,
        // original content type — no serialization re-encoding.
        Response.StatusCode = result.HttpStatus;
        Response.ContentType = result.ContentType;
        await Response.Body.WriteAsync(result.Body, ct);
        return new EmptyResult();
    }

    /// <summary>Bounded read of the incoming request body — in-memory only, disposed after forward.</summary>
    private async Task<byte[]> ReadBoundedBodyAsync(CancellationToken ct)
    {
        if (Request.ContentLength is null or 0)
        {
            return [];
        }

        if (Request.ContentLength > EkycProxyService.MaxRequestBodyBytes)
        {
            throw new MedFlow.Domain.Exceptions.ValidationException("Payload vượt quá kích thước cho phép.");
        }

        using var buffer = new MemoryStream(
            capacity: (int)Math.Min(Request.ContentLength ?? 0, EkycProxyService.MaxRequestBodyBytes));

        var chunk = new byte[64 * 1024];
        var total = 0;
        while (true)
        {
            var read = await Request.Body.ReadAsync(chunk.AsMemory(0, chunk.Length), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > EkycProxyService.MaxRequestBodyBytes)
            {
                throw new MedFlow.Domain.Exceptions.ValidationException("Payload vượt quá kích thước cho phép.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }

    private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
}
