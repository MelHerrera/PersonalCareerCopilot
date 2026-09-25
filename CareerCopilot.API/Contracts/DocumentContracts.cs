using CareerCopilot.API.Models;

namespace CareerCopilot.API.Contracts;

/// <param name="Format">"pdf" o "text" (pegado o importado desde un link).</param>
/// <param name="SourceUrl">Link del que se importó el documento, si aplica.</param>
public record DocumentResponse(
    string Name,
    DocumentKind Kind,
    string Format,
    long SizeBytes,
    DateTimeOffset? UploadedAt,
    int ChunkCount,
    bool IsIndexed,
    string? SourceUrl);

/// <param name="Text">El texto de la vacante (o del documento) tal cual se copió.</param>
/// <param name="Title">Nombre opcional; si falta, se usa la primera línea del texto.</param>
/// <param name="Kind">resume | jobPosting | other. Por defecto, jobPosting.</param>
public record AddTextDocumentRequest(string Text, string? Title = null, string? Kind = null);

/// <param name="Url">Link a la vacante (página web o PDF).</param>
/// <param name="Kind">resume | jobPosting | other. Por defecto, jobPosting.</param>
public record ImportDocumentRequest(string Url, string? Kind = null);
