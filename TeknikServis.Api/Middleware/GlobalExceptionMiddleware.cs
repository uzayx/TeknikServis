using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Common.Exceptions;

namespace TeknikServis.Api.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
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

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, errorCode) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Kayit bulunamadi", "NOT_FOUND"),
            BusinessRuleException bre => (StatusCodes.Status409Conflict, "Is kurali ihlali", bre.ErrorCode),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Eszamanlilik cakismasi", "CONCURRENCY_CONFLICT"),
            DbUpdateException => (StatusCodes.Status409Conflict, "Veri butunlugu ihlali", "DATA_INTEGRITY_VIOLATION"),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen hata", "INTERNAL_ERROR")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Beklenmeyen hata. Path: {Path}", context.Request.Path);
        }
        else
        {
            _logger.LogWarning("Is hatasi ({ErrorCode}): {Message}. Path: {Path}",
                errorCode, exception.Message, context.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = statusCode == StatusCodes.Status500InternalServerError
                ? "Beklenmeyen bir hata olustu. Lutfen daha sonra tekrar deneyin."
                : exception.Message,
            Instance = context.Request.Path
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
