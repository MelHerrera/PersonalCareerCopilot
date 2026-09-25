using System.Runtime.CompilerServices;
using Azure.AI.OpenAI;
using CareerCopilot.API.Contracts;
using CareerCopilot.API.Models;
using OpenAI.Chat;
using ChatMessage = OpenAI.Chat.ChatMessage;

namespace CareerCopilot.API.Services;

public interface ICareerChatService
{
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request, CancellationToken ct = default);
}

public class CareerChatService : ICareerChatService
{
    private const int TopK = 5;
    private const int MaxHistoryTurns = 10;
    private const int ExcerptLength = 280;

    private readonly ChatClient _chatClient;
    private readonly IEmbeddingService _embeddings;
    private readonly IVectorStore _vectorStore;

    private const string SystemPrompt =
        "Eres un asistente de carrera que ayuda a Mel con su búsqueda de empleo. " +
        "Responde SOLO con base en el contexto de documentos que se te proporciona " +
        "(su CV y descripciones de vacantes). Si el contexto no alcanza para responder, dilo claramente. " +
        "Responde en el mismo idioma de la pregunta y usa Markdown (títulos cortos, listas, negritas) " +
        "cuando ayude a la lectura.";

    public CareerChatService(AzureOpenAIClient azureClient, IConfiguration config, IEmbeddingService embeddings, IVectorStore vectorStore)
    {
        var deployment = config["AzureOpenAI:ChatDeployment"] ?? "gpt-4o-mini";
        _chatClient = azureClient.GetChatClient(deployment);
        _embeddings = embeddings;
        _vectorStore = vectorStore;
    }

    public async Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct = default)
    {
        var (messages, sources) = await BuildPromptAsync(request, ct);
        var result = await _chatClient.CompleteChatAsync(messages, cancellationToken: ct);
        return new ChatResponse(result.Value.Content[0].Text, sources);
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (messages, sources) = await BuildPromptAsync(request, ct);
        yield return ChatStreamEvent.ForSources(sources);

        await foreach (var update in _chatClient.CompleteChatStreamingAsync(messages, cancellationToken: ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return ChatStreamEvent.ForDelta(part.Text);
            }
        }

        yield return ChatStreamEvent.Done;
    }

    // RAG: embebe la pregunta, recupera los fragmentos más relevantes y arma los mensajes.
    private async Task<(List<ChatMessage> Messages, IReadOnlyList<ChatSource> Sources)> BuildPromptAsync(
        ChatRequest request, CancellationToken ct)
    {
        var queryEmbedding = await _embeddings.EmbedAsync(request.Question, ct);
        var relevantChunks = _vectorStore.Search(queryEmbedding, TopK);

        var context = relevantChunks.Count == 0
            ? "(todavía no hay documentos indexados)"
            : string.Join("\n---\n", relevantChunks.Select(c =>
                $"[{c.Chunk.Kind.ToLabel()}: {c.Chunk.SourceFile}]\n{c.Chunk.Text}"));

        var messages = new List<ChatMessage> { new SystemChatMessage(SystemPrompt) };
        foreach (var turn in (request.History ?? []).TakeLast(MaxHistoryTurns))
        {
            messages.Add(turn.Role == ChatRole.Assistant
                ? new AssistantChatMessage(turn.Content)
                : new UserChatMessage(turn.Content));
        }
        messages.Add(new UserChatMessage($"Contexto:\n{context}\n\nPregunta: {request.Question}"));

        // Una fuente por archivo, con su fragmento más relevante.
        var sources = relevantChunks
            .GroupBy(c => c.Chunk.SourceFile)
            .Select(g => g.MaxBy(c => c.Score)!)
            .Select(c => new ChatSource(c.Chunk.SourceFile, c.Chunk.Kind, ToExcerpt(c.Chunk.Text), Math.Round(c.Score, 3)))
            .ToList();

        return (messages, sources);
    }

    private static string ToExcerpt(string text)
    {
        var singleLine = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= ExcerptLength ? singleLine : singleLine[..ExcerptLength].TrimEnd() + "…";
    }
}
