using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using CaresPharma_Insights.Shared.Dtos;

namespace CaresPharma_Insights.Api.Middlewares
{
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly Dictionary<Type, Func<HttpContext, Exception, CancellationToken, Task>> _exceptionHandlers;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;

            _exceptionHandlers = new Dictionary<Type, Func<HttpContext, Exception, CancellationToken, Task>>
            {
                { typeof(ArgumentNullException), HandleArgumentNullException },
                { typeof(ArgumentException), HandleArgumentException },
                { typeof(UnauthorizedAccessException), HandleUnauthorizedAccessException },
                { typeof(OperationCanceledException), HandleOperationCanceledException }
            };
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            Type exceptionType = exception.GetType();

            if (_exceptionHandlers.TryGetValue(exceptionType, out Func<HttpContext, Exception, CancellationToken, Task>? handler))
            {
                _logger.LogWarning(exception, "Handled known exception of type: {ExceptionType}", exceptionType.Name);
                await handler(httpContext, exception, cancellationToken);
                return true;
            }

            _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

            httpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            httpContext.Response.ContentType = "application/json";

            ChatResponse response = new ChatResponse(
                Reply: "An unexpected error occurred while processing your request. Please try again later.",
                Success: false,
                Error: exception.Message
            );

            await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
            return true;
        }

        private static async Task HandleArgumentNullException(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            ChatResponse response = new ChatResponse(
                Reply: "Invalid request: Required argument was not provided.",
                Success: false,
                Error: exception.Message
            );
            await context.Response.WriteAsJsonAsync(response, cancellationToken);
        }

        private static async Task HandleArgumentException(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            ChatResponse response = new ChatResponse(
                Reply: "Invalid request parameter.",
                Success: false,
                Error: exception.Message
            );
            await context.Response.WriteAsJsonAsync(response, cancellationToken);
        }

        private static async Task HandleUnauthorizedAccessException(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            ChatResponse response = new ChatResponse(
                Reply: "Unauthorized: Access denied.",
                Success: false,
                Error: exception.Message
            );
            await context.Response.WriteAsJsonAsync(response, cancellationToken);
        }

        private static async Task HandleOperationCanceledException(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            context.Response.StatusCode = 499; // Client Closed Request
            ChatResponse response = new ChatResponse(
                Reply: "The operation was canceled by the client.",
                Success: false,
                Error: exception.Message
            );
            await context.Response.WriteAsJsonAsync(response, cancellationToken);
        }
    }
}
