using Azure.AI.OpenAI;
using OpenAI.Embeddings;

namespace CareerCopilot.API.Services;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

public class EmbeddingService : IEmbeddingService
{
    // Límite conservador de entradas por request al endpoint de embeddings.
    private const int BatchSize = 64;

    private readonly EmbeddingClient _client;

    public EmbeddingService(AzureOpenAIClient azureClient, IConfiguration config)
    {
        var deployment = config["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";
        _client = azureClient.GetEmbeddingClient(deployment);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var result = await _client.GenerateEmbeddingsAsync(batch, cancellationToken: ct);
            vectors.AddRange(result.Value.OrderBy(e => e.Index).Select(e => e.ToFloats().ToArray()));
        }
        return vectors;
    }
}
