using CareerCopilot.API.Models;

namespace CareerCopilot.API.Contracts;

public enum ChatRole
{
    User,
    Assistant
}

public record ChatTurn(ChatRole Role, string Content);

/// <param name="Question">La pregunta actual.</param>
/// <param name="History">Turnos anteriores de la conversación (opcional) para preguntas de seguimiento.</param>
public record ChatRequest(string Question, IReadOnlyList<ChatTurn>? History = null);

/// <summary>Fragmento de un documento que se usó como contexto para responder.</summary>
public record ChatSource(string FileName, DocumentKind Kind, string Excerpt, double Score);

public record ChatResponse(string Answer, IReadOnlyList<ChatSource> Sources);

/// <summary>
/// Evento del stream SSE de /api/chat/stream. Orden: un "sources", N "delta", y "done" (o "error").
/// </summary>
public record ChatStreamEvent(string Type, string? Text = null, IReadOnlyList<ChatSource>? Sources = null)
{
    public static ChatStreamEvent ForSources(IReadOnlyList<ChatSource> sources) => new("sources", Sources: sources);
    public static ChatStreamEvent ForDelta(string text) => new("delta", Text: text);
    public static ChatStreamEvent ForError(string message) => new("error", Text: message);
    public static readonly ChatStreamEvent Done = new("done");
}
