using Microsoft.AspNetCore.Http.HttpResults;

namespace CareerCopilot.API.Endpoints;

public record HealthResponse(string Status, bool AzureOpenAIConfigured, bool BlobStorageConfigured);

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder routes)
    {
        // GET /api/health
        // Le permite al front avisar si falta configurar Azure OpenAI o el Storage.
        routes.MapGet("/api/health", Ok<HealthResponse> (IConfiguration config) =>
            TypedResults.Ok(new HealthResponse(
                "ok",
                AzureOpenAIConfigured: IsSet(config["AzureOpenAI:Endpoint"]) && IsSet(config["AzureOpenAI:ApiKey"]),
                BlobStorageConfigured: IsSet(config["BlobStorage:ConnectionString"]))))
            .WithTags("System")
            .WithName("Health");

        return routes;
    }

    // Los valores de ejemplo de appsettings empiezan con "TU-" (TU-RECURSO, TU-API-KEY).
    private static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains("TU-", StringComparison.Ordinal);
}
