using System.Text.RegularExpressions;
using CareerCopilot.API.Contracts;
using CareerCopilot.API.Models;
using CareerCopilot.API.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CareerCopilot.API.Endpoints;

public static partial class DocumentEndpoints
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int MinTextLength = 80;
    private const int MaxTextLength = 200_000;

    public static RouteGroupBuilder MapDocumentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/documents").WithTags("Documents");

        group.MapGet("/", ListDocuments).WithName("ListDocuments");
        group.MapGet("/{name}", GetDocument).WithName("GetDocument");
        group.MapGet("/{name}/file", DownloadDocument).WithName("DownloadDocument");
        group.MapPost("/", UploadDocument).WithName("UploadDocument").DisableAntiforgery();
        group.MapPost("/text", AddTextDocument).WithName("AddTextDocument");
        group.MapPost("/import", ImportDocument).WithName("ImportDocument");
        group.MapPost("/{name}/reindex", ReindexDocument).WithName("ReindexDocument");
        group.MapDelete("/{name}", DeleteDocument).WithName("DeleteDocument");

        return group;
    }

    // GET /api/documents
    // Lista los documentos guardados en Blob Storage junto con su estado de indexación.
    private static async Task<Ok<List<DocumentResponse>>> ListDocuments(
        IBlobStorageService blobStorage, IVectorStore vectorStore, CancellationToken ct)
    {
        var chunkCounts = vectorStore.GetChunkCountsBySource();
        var documents = await blobStorage.ListAsync(ct);

        return TypedResults.Ok(documents
            .OrderByDescending(d => d.LastModified)
            .Select(d => ToResponse(d, chunkCounts))
            .ToList());
    }

    // GET /api/documents/{name}
    private static async Task<Results<Ok<DocumentResponse>, NotFound>> GetDocument(
        string name, IBlobStorageService blobStorage, IVectorStore vectorStore, CancellationToken ct)
    {
        var document = await blobStorage.GetAsync(name, ct);
        return document is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToResponse(document, vectorStore.GetChunkCountsBySource()));
    }

    // GET /api/documents/{name}/file
    // Devuelve el documento original (PDF o texto) para verlo o descargarlo desde el front.
    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadDocument(
        string name, IBlobStorageService blobStorage, CancellationToken ct)
    {
        var download = await blobStorage.DownloadAsync(name, ct);
        return download is null
            ? TypedResults.NotFound()
            : TypedResults.File(download.Content, download.ContentType, enableRangeProcessing: false);
    }

    // POST /api/documents (multipart/form-data: "file" + "kind" opcional: resume | jobPosting | other)
    // Sube un PDF a Blob Storage, extrae su texto, lo trocea, genera embeddings
    // y lo indexa en el vector store para poder consultarlo después.
    private static async Task<Results<Created<DocumentResponse>, ValidationProblem, UnprocessableEntity<ProblemDetails>>> UploadDocument(
        IFormFile file,
        [FromForm] string? kind,
        IDocumentIngestionService ingestion,
        IBlobStorageService blobStorage,
        IVectorStore vectorStore,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var fileName = Path.GetFileName(file.FileName);

        if (file.Length == 0)
            errors["file"] = ["The file is empty."];
        else if (file.Length > MaxUploadBytes)
            errors["file"] = [$"The file exceeds the {MaxUploadBytes / 1024 / 1024} MB limit."];
        else if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            errors["file"] = ["Only PDF files are supported."];

        if (!TryParseKind(kind, DocumentKind.Other, out var documentKind))
            errors["kind"] = ["Kind must be one of: resume, jobPosting, other."];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        try
        {
            await using var stream = file.OpenReadStream();
            await ingestion.IngestPdfAsync(fileName, stream, documentKind, ct: ct);
        }
        catch (Exception ex) when (IsUnreadablePdf(ex))
        {
            loggerFactory.CreateLogger(nameof(DocumentEndpoints)).LogWarning(ex, "No se pudo leer el PDF {FileName}", fileName);
            return UnreadablePdf();
        }

        return await CreatedAsync(fileName, blobStorage, vectorStore, ct);
    }

    // POST /api/documents/text  { "text": "...", "title": "opcional", "kind": "jobPosting" }
    // Para vacantes copiadas y pegadas: se guardan como .txt y se indexan igual que un PDF.
    private static async Task<Results<Created<DocumentResponse>, ValidationProblem>> AddTextDocument(
        AddTextDocumentRequest request,
        IDocumentIngestionService ingestion,
        IBlobStorageService blobStorage,
        IVectorStore vectorStore,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var text = request.Text?.Trim() ?? "";

        if (text.Length < MinTextLength)
            errors["text"] = ["That's too short to be a job posting. Paste the full text."];
        else if (text.Length > MaxTextLength)
            errors["text"] = ["That text is too long. Paste just the posting."];

        if (!TryParseKind(request.Kind, DocumentKind.JobPosting, out var kind))
            errors["kind"] = ["Kind must be one of: resume, jobPosting, other."];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var title = string.IsNullOrWhiteSpace(request.Title) ? FirstLine(text) : request.Title;
        var fileName = ToFileName(title, ".txt");

        await ingestion.IngestTextAsync(fileName, text, kind, ct: ct);
        return await CreatedAsync(fileName, blobStorage, vectorStore, ct);
    }

    // POST /api/documents/import  { "url": "https://...", "kind": "jobPosting" }
    // Lee la vacante desde un link (página web o PDF) y la indexa.
    private static async Task<Results<Created<DocumentResponse>, ValidationProblem, UnprocessableEntity<ProblemDetails>>> ImportDocument(
        ImportDocumentRequest request,
        IWebPageImporter importer,
        IDocumentIngestionService ingestion,
        IBlobStorageService blobStorage,
        IVectorStore vectorStore,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(url.UserInfo))
            errors["url"] = ["Enter a full web address that starts with http:// or https://."];

        if (!TryParseKind(request.Kind, DocumentKind.JobPosting, out var kind))
            errors["kind"] = ["Kind must be one of: resume, jobPosting, other."];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var logger = loggerFactory.CreateLogger(nameof(DocumentEndpoints));
        string fileName;
        try
        {
            var page = await importer.ImportAsync(url!, ct);
            if (page.Pdf is not null)
            {
                fileName = ToFileName(page.Title, ".pdf");
                await using var pdf = new MemoryStream(page.Pdf);
                await ingestion.IngestPdfAsync(fileName, pdf, kind, url!.AbsoluteUri, ct);
            }
            else
            {
                fileName = ToFileName(page.Title, ".txt");
                await ingestion.IngestTextAsync(fileName, $"{page.Text}\n\nSource: {url!.AbsoluteUri}", kind, url.AbsoluteUri, ct);
            }
        }
        catch (ImportException ex)
        {
            logger.LogInformation(ex, "No se pudo importar {Url}", url);
            return TypedResults.UnprocessableEntity(new ProblemDetails
            {
                Title = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
        catch (Exception ex) when (IsUnreadablePdf(ex))
        {
            logger.LogWarning(ex, "El link {Url} apunta a un PDF ilegible", url);
            return UnreadablePdf();
        }

        return await CreatedAsync(fileName, blobStorage, vectorStore, ct);
    }

    // POST /api/documents/{name}/reindex
    // El vector store vive en memoria: tras reiniciar la API los documentos siguen en Blob Storage
    // pero hay que volver a indexarlos.
    private static async Task<Results<Ok<DocumentResponse>, NotFound>> ReindexDocument(
        string name, IDocumentIngestionService ingestion, IBlobStorageService blobStorage, IVectorStore vectorStore, CancellationToken ct)
    {
        var chunks = await ingestion.ReindexAsync(name, ct);
        if (chunks is null) return TypedResults.NotFound();

        var document = await blobStorage.GetAsync(name, ct);
        return document is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToResponse(document, vectorStore.GetChunkCountsBySource()));
    }

    // DELETE /api/documents/{name}
    private static async Task<Results<NoContent, NotFound>> DeleteDocument(
        string name, IBlobStorageService blobStorage, IVectorStore vectorStore, CancellationToken ct)
    {
        vectorStore.RemoveBySource(name);
        return await blobStorage.DeleteAsync(name, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Created<DocumentResponse>> CreatedAsync(
        string fileName, IBlobStorageService blobStorage, IVectorStore vectorStore, CancellationToken ct)
    {
        var stored = await blobStorage.GetAsync(fileName, ct)
            ?? throw new InvalidOperationException($"'{fileName}' no aparece en Blob Storage después de guardarlo.");
        return TypedResults.Created(
            $"/api/documents/{Uri.EscapeDataString(fileName)}",
            ToResponse(stored, vectorStore.GetChunkCountsBySource()));
    }

    private static DocumentResponse ToResponse(StoredDocument document, IReadOnlyDictionary<string, int> chunkCounts)
    {
        var chunkCount = chunkCounts.GetValueOrDefault(document.Name);
        var format = document.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? "text" : "pdf";
        return new DocumentResponse(document.Name, document.Kind, format, document.SizeBytes, document.LastModified,
            chunkCount, chunkCount > 0, document.SourceUrl);
    }

    private static bool TryParseKind(string? value, DocumentKind fallback, out DocumentKind kind)
    {
        kind = fallback;
        return string.IsNullOrWhiteSpace(value) || Enum.TryParse(value, ignoreCase: true, out kind);
    }

    private static bool IsUnreadablePdf(Exception ex) =>
        ex is UglyToad.PdfPig.Core.PdfDocumentFormatException or InvalidOperationException;

    private static UnprocessableEntity<ProblemDetails> UnreadablePdf() =>
        TypedResults.UnprocessableEntity(new ProblemDetails
        {
            Title = "The PDF could not be read",
            Detail = "Make sure the file is a valid, text-based (not scanned) PDF.",
            Status = StatusCodes.Status422UnprocessableEntity
        });

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Pasted document";

    // Nombre de archivo legible a partir de un título: sin caracteres problemáticos para
    // blobs o URLs, espacios colapsados y un largo razonable.
    private static string ToFileName(string title, string extension)
    {
        var cleaned = UnsafeFileNameChars().Replace(title, " ");
        cleaned = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (cleaned.Length > 80) cleaned = cleaned[..80].TrimEnd(' ', '.');
        if (cleaned.Length == 0) cleaned = "Pasted document";
        return cleaned.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? cleaned : cleaned + extension;
    }

    [GeneratedRegex(@"[\\/:*?""<>|#%&{}\p{C}]")]
    private static partial Regex UnsafeFileNameChars();
}
