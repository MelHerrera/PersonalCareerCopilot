using System.Text;
using CareerCopilot.API.Models;

namespace CareerCopilot.API.Services;

public interface IDocumentIngestionService
{
    /// <summary>Guarda el PDF en Blob Storage y lo indexa. Devuelve los fragmentos indexados.</summary>
    Task<int> IngestPdfAsync(string fileName, Stream pdf, DocumentKind kind, string? sourceUrl = null, CancellationToken ct = default);

    /// <summary>Guarda texto plano (pegado o importado desde un link) como .txt y lo indexa.</summary>
    Task<int> IngestTextAsync(string fileName, string text, DocumentKind kind, string? sourceUrl = null, CancellationToken ct = default);

    /// <summary>Vuelve a indexar un documento que ya está en Blob Storage (ej. tras reiniciar la API).</summary>
    Task<int?> ReindexAsync(string fileName, CancellationToken ct = default);
}

public class DocumentIngestionService(
    IBlobStorageService blobStorage,
    IPdfTextExtractor pdfExtractor,
    IEmbeddingService embeddings,
    IVectorStore vectorStore) : IDocumentIngestionService
{
    public const string PdfContentType = "application/pdf";
    public const string TextContentType = "text/plain; charset=utf-8";

    public async Task<int> IngestPdfAsync(string fileName, Stream pdf, DocumentKind kind, string? sourceUrl = null, CancellationToken ct = default)
    {
        await using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, ct);

        // Primero se extrae el texto: si el PDF no es válido, no se guarda nada.
        buffer.Position = 0;
        var text = pdfExtractor.ExtractText(buffer);

        buffer.Position = 0;
        await blobStorage.UploadAsync(new DocumentUpload(fileName, kind, PdfContentType, sourceUrl), buffer, ct);

        return await IndexAsync(fileName, kind, text, ct);
    }

    public async Task<int> IngestTextAsync(string fileName, string text, DocumentKind kind, string? sourceUrl = null, CancellationToken ct = default)
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await blobStorage.UploadAsync(new DocumentUpload(fileName, kind, TextContentType, sourceUrl), content, ct);

        return await IndexAsync(fileName, kind, text, ct);
    }

    public async Task<int?> ReindexAsync(string fileName, CancellationToken ct = default)
    {
        var document = await blobStorage.GetAsync(fileName, ct);
        if (document is null) return null;

        var download = await blobStorage.DownloadAsync(fileName, ct);
        if (download is null) return null;

        await using var buffer = new MemoryStream();
        await using (download.Content)
        {
            await download.Content.CopyToAsync(buffer, ct);
        }
        buffer.Position = 0;

        var text = download.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(buffer.ToArray())
            : pdfExtractor.ExtractText(buffer);

        return await IndexAsync(fileName, document.Kind, text, ct);
    }

    private async Task<int> IndexAsync(string fileName, DocumentKind kind, string text, CancellationToken ct)
    {
        var chunks = TextChunker.Split(text);
        var vectors = chunks.Count == 0 ? [] : await embeddings.EmbedManyAsync(chunks, ct);

        vectorStore.RemoveBySource(fileName); // por si se vuelve a subir el mismo archivo
        vectorStore.AddRange(chunks.Select((chunkText, i) => new DocumentChunk
        {
            Id = $"{fileName}#{i}",
            SourceFile = fileName,
            Kind = kind,
            Text = chunkText,
            Embedding = vectors[i]
        }));

        return chunks.Count;
    }
}
