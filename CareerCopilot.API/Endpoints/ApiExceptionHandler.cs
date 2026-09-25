using System.ClientModel;
using Azure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CareerCopilot.API.Endpoints;

// Traduce las excepciones de los servicios de Azure a respuestas ProblemDetails
// con mensajes que el front puede mostrar tal cual.
public class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Error no controlado en {Path}", httpContext.Request.Path);

        var problem = Describe(exception);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    // Los SDKs de Azure envuelven los fallos en AggregateException tras agotar los reintentos.
    public static ProblemDetails Describe(Exception exception) => exception switch
    {
        AggregateException { InnerException: { } inner } => Describe(inner),
        RequestFailedException => new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Blob Storage is unavailable",
            Detail = "Check BlobStorage:ConnectionString, or start Azurite if you are running locally."
        },
        ClientResultException { InnerException: HttpRequestException } => new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title = "Could not reach the AI service",
            Detail = "Check AzureOpenAI:Endpoint in your configuration."
        },
        ClientResultException => new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title = "The AI service returned an error",
            Detail = "Check the AzureOpenAI endpoint, API key and deployment names."
        },
        HttpRequestException => new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title = "Could not reach an Azure service",
            Detail = exception.Message
        },
        _ => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Something went wrong on the server"
        }
    };
}
