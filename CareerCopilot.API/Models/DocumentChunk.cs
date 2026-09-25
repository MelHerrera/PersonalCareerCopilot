namespace CareerCopilot.API.Models;

public class DocumentChunk
{
    public required string Id { get; init; }
    public required string SourceFile { get; init; }
    public required DocumentKind Kind { get; init; }
    public required string Text { get; init; }
    public required float[] Embedding { get; init; }
}
