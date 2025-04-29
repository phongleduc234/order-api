using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OrderApi.Shared.Exceptions;

namespace OrderApi.Shared.Middleware
{
    public class ValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public ValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (ValidationException ex)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";

                var response = new
                {
                    StatusCode = context.Response.StatusCode,
                    Message = ex.Message,
                    Errors = ex.Errors
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            }
        }
    }
} 