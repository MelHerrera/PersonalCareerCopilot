using CareerCopilot.API.Models;

namespace CareerCopilot.API.Services;

public record ScoredChunk(DocumentChunk Chunk, double Score);

public interface IVectorStore
{
    void AddRange(IEnumerable<DocumentChunk> chunks);
    IReadOnlyList<ScoredChunk> Search(float[] queryEmbedding, int topK = 4);
    void RemoveBySource(string sourceFile);
    IReadOnlyDictionary<string, int> GetChunkCountsBySource();
}

// Almacén en memoria, simple a propósito: cuando en el curso vean Azure AI Search
// (búsqueda vectorial real), esta es la única pieza que se reemplaza, implementando
// la misma interfaz, sin tocar el resto de la app.
public class InMemoryVectorStore : IVectorStore
{
    private readonly List<DocumentChunk> _chunks = new();
    private readonly object _lock = new();

    public void AddRange(IEnumerable<DocumentChunk> chunks)
    {
        lock (_lock)
        {
            _chunks.AddRange(chunks);
        }
    }

    public void RemoveBySource(string sourceFile)
    {
        lock (_lock)
        {
            _chunks.RemoveAll(c => c.SourceFile == sourceFile);
        }
    }

    public IReadOnlyDictionary<string, int> GetChunkCountsBySource()
    {
        lock (_lock)
        {
            return _chunks.GroupBy(c => c.SourceFile).ToDictionary(g => g.Key, g => g.Count());
        }
    }

    public IReadOnlyList<ScoredChunk> Search(float[] queryEmbedding, int topK = 4)
    {
        lock (_lock)
        {
            return _chunks
                .Select(c => new ScoredChunk(c, CosineSimilarity(c.Embedding, queryEmbedding)))
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .ToList();
        }
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB) + 1e-8);
    }
}
