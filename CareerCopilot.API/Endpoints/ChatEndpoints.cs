using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using CareerCopilot.API.Contracts;
using CareerCopilot.API.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CareerCopilot.API.Endpoints;

public static class ChatEndpoints
{
    public static RouteGroupBuilder MapChatEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/", Ask).WithName("Ask");
        group.MapPost("/stream", Stream).WithName("AskStream");

        return group;
    }

    // POST /api/chat
    // Pregunta contra los documentos indexados (RAG) y devuelve la respuesta completa.
    private static async Task<Results<Ok<ChatResponse>, ValidationProblem>> Ask(
        ChatRequest request, ICareerChatService chatService, CancellationToken ct)
    {
        if (Validate(request) is { } problem) return problem;
        return TypedResults.Ok(await chatService.AskAsync(request, ct));
    }

    // POST /api/chat/stream
    // Igual que /api/chat, pero la respuesta llega token a token como Server-Sent Events.
    private static Results<ServerSentEventsResult<ChatStreamEvent>, ValidationProblem> Stream(
        ChatRequest request, ICareerChatService chatService, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (Validate(request) is { } problem) return problem;

        var logger = loggerFactory.CreateLogger(nameof(ChatEndpoints));
        var events = WithErrorEvent(chatService.StreamAsync(request, ct), logger, ct);
        return TypedResults.ServerSentEvents(events.Select(e => new SseItem<ChatStreamEvent>(e, e.Type)));
    }

    private static ValidationProblem? Validate(ChatRequest request) =>
        string.IsNullOrWhiteSpace(request.Question)
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = ["The question cannot be empty."]
            })
            : null;

    // Una vez que el stream empezó ya no se puede devolver un status de error,
    // así que los fallos se envían como un evento "error".
    private static async IAsyncEnumerable<ChatStreamEvent> WithErrorEvent(
        IAsyncEnumerable<ChatStreamEvent> source, ILogger logger, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var enumerator = source.GetAsyncEnumerator(ct);
        while (true)
        {
            ChatStreamEvent current;
            string? error = null;
            try
            {
                if (!await enumerator.MoveNextAsync()) yield break;
                current = enumerator.Current;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error durante el streaming del chat");
                error = ApiExceptionHandler.Describe(ex).Title;
                current = ChatStreamEvent.Done;
            }

            if (error is not null)
            {
                yield return ChatStreamEvent.ForError(error);
                yield break;
            }
            yield return current;
        }
    }
}
