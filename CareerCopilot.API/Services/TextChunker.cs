namespace CareerCopilot.API.Services;

public static class TextChunker
{
    /// <summary>
    /// Trocea el texto en fragmentos de ~chunkSize caracteres, con solape (overlap)
    /// para no cortar ideas a la mitad entre un fragmento y el siguiente.
    /// </summary>
    public static List<string> Split(string text, int chunkSize = 800, int overlap = 100)
    {
        var chunks = new List<string>();
        var normalized = text.Replace("\r\n", "\n").Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return chunks;

        var position = 0;
        while (position < normalized.Length)
        {
            var length = Math.Min(chunkSize, normalized.Length - position);
            var chunk = normalized.Substring(position, length).Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);

            position += chunkSize - overlap;
        }

        return chunks;
    }
}