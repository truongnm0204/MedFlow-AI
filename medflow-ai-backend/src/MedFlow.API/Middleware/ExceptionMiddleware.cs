using System.Net;
using System.Text.Json;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var (statusCode, problemDetails) = ex switch
        {
            ValidationException ve => (HttpStatusCode.BadRequest,
                new ProblemDetails { Status = 400, Title = "Lỗi xác thực", Detail = ve.Message }),
            NotFoundException nf => (HttpStatusCode.NotFound,
                new ProblemDetails { Status = 404, Title = "Không tìm thấy", Detail = nf.Message }),
            UnauthorizedAccessException ua => (HttpStatusCode.Unauthorized,
                new ProblemDetails { Status = 401, Title = "Không được phép", Detail = ua.Message }),
            _ => (HttpStatusCode.InternalServerError,
                new ProblemDetails { Status = 500, Title = "Lỗi hệ thống", Detail = "Đã xảy ra lỗi không mong muốn" }),
        };

        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(ex, "Unhandled exception");

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
    }
}
