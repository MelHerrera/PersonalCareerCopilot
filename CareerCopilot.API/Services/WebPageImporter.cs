using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace CareerCopilot.API.Services;

/// <summary>Lo que se pudo leer de un link: texto listo para indexar, o un PDF tal cual.</summary>
public record ImportedPage(string Title, string? Text, byte[]? Pdf);

/// <summary>Error con un mensaje apto para mostrarle al usuario tal cual.</summary>
public class ImportException(string message, Exception? inner = null) : Exception(message, inner);

public interface IWebPageImporter
{
    Task<ImportedPage> ImportAsync(Uri url, CancellationToken ct = default);
}

// Lee una vacante desde un link. La mayoría de portales (LinkedIn, Indeed, Greenhouse, Lever,
// Workday…) publican la vacante como JSON-LD schema.org/JobPosting para Google Jobs; si está,
// es la fuente más limpia. Si no, se usa el texto principal de la página.
public class WebPageImporter(HttpClient http) : IWebPageImporter
{
    public const long MaxResponseBytes = 5 * 1024 * 1024;
    private const int MinUsefulTextLength = 200;

    private static readonly string[] NoiseSelectors =
        ["script", "style", "noscript", "svg", "template", "iframe", "nav", "header", "footer", "form", "aside", "button"];

    // Palabras que casi toda vacante usa (inglés y español).
    private static readonly string[] PostingSignals =
    [
        "responsibilit", "requirement", "qualification", "experience", "you will", "you'll", "what you",
        "skills", "apply", "salary", "benefits",
        "responsabilidades", "requisitos", "experiencia", "habilidades", "postúlate", "postular", "beneficios", "salario"
    ];

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "main", "li", "ul", "ol", "br", "tr", "table",
        "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "dd", "dt", "dl"
    };

    public async Task<ImportedPage> ImportAsync(Uri url, CancellationToken ct = default)
    {
        using var response = await SendAsync(url, ct);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
        var bytes = await ReadLimitedAsync(response.Content, ct);

        if (mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return new ImportedPage(TitleFromUrl(url), null, bytes);

        if (!mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new ImportException("That link doesn't point to a web page or a PDF.");

        var html = DecodeBody(bytes, response.Content.Headers.ContentType);
        var document = new HtmlParser().ParseDocument(html);

        if (FromJobPostingJsonLd(document) is { } posting)
            return posting;

        // Sin datos estructurados, el texto tiene que parecer una vacante: si no, suele ser
        // una vacante cerrada que redirige a la página general de empleos, o un muro de login.
        var page = FromPageContent(document, url);
        if (page.Text is null || page.Text.Length < MinUsefulTextLength || !LooksLikeJobPosting(page.Text))
            throw new ImportException(
                "That page doesn't look like a job posting. It may have closed, or the site only shows it after you sign in. Paste the posting's text instead.");

        return page;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException)
        {
            throw new ImportException("That link points to a private or local address, which isn't allowed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ImportException("Couldn't reach that link. Check the address and try again.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ImportException("That page took too long to respond.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new ImportException(status is 401 or 403 or 999
                ? "That site blocked the request. Paste the posting's text instead."
                : $"That page answered with an error ({status}).");
        }

        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            response.Dispose();
            throw new ImportException("That page is too large to import.");
        }

        return response;
    }

    // Content-Length puede faltar (respuestas chunked), así que el límite se aplica al leer.
    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
                throw new ImportException("That page is too large to import.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static string DecodeBody(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        try
        {
            var charset = contentType?.CharSet?.Trim('"');
            return (charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset)).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    // --- JSON-LD schema.org/JobPosting --------------------------------------------------

    private static ImportedPage? FromJobPostingJsonLd(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(script.TextContent, new JsonDocumentOptions { AllowTrailingCommas = true });
            }
            catch (JsonException)
            {
                continue;
            }

            using (json)
            {
                if (FindJobPosting(json.RootElement) is { } posting)
                    return ToImportedPage(posting);
            }
        }
        return null;
    }

    private static JsonElement? FindJobPosting(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (FindJobPosting(item) is { } found) return found;
                break;

            case JsonValueKind.Object:
                if (element.TryGetProperty("@type", out var type) && IsJobPostingType(type))
                    return element;
                if (element.TryGetProperty("@graph", out var graph))
                    return FindJobPosting(graph);
                break;
        }
        return null;
    }

    private static bool IsJobPostingType(JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => type.GetString() == "JobPosting",
        JsonValueKind.Array => type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "JobPosting"),
        _ => false
    };

    private static ImportedPage ToImportedPage(JsonElement posting)
    {
        var title = GetString(posting, "title") ?? "Job posting";
        var company = posting.TryGetProperty("hiringOrganization", out var org)
            ? org.ValueKind == JsonValueKind.Object ? GetString(org, "name") : org.ValueKind == JsonValueKind.String ? org.GetString() : null
            : null;

        var text = new StringBuilder();
        text.AppendLine(company is null ? title : $"{title} at {company}");
        AppendIfPresent(text, "Location", Location(posting));
        AppendIfPresent(text, "Employment type", GetStringOrList(posting, "employmentType"));
        text.AppendLine();

        foreach (var field in new[] { "description", "responsibilities", "qualifications", "skills", "experienceRequirements", "educationRequirements" })
        {
            var value = GetStringOrList(posting, field);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (field != "description") text.AppendLine().AppendLine(Humanize(field));
            text.AppendLine(HtmlFragmentToText(value));
        }

        return new ImportedPage(company is null ? title : $"{title} at {company}", text.ToString().Trim(), null);
    }

    private static string? Location(JsonElement posting)
    {
        if (!posting.TryGetProperty("jobLocation", out var location)) return null;
        var first = location.ValueKind == JsonValueKind.Array ? location.EnumerateArray().FirstOrDefault() : location;
        if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("address", out var address)) return null;
        if (address.ValueKind == JsonValueKind.String) return address.GetString();

        var parts = new[] { "addressLocality", "addressRegion", "addressCountry" }
            .Select(p => address.TryGetProperty(p, out var v)
                ? v.ValueKind == JsonValueKind.Object ? GetString(v, "name") : v.ValueKind == JsonValueKind.String ? v.GetString() : null
                : null)
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(", ", parts);
        return joined.Length > 0 ? joined : null;
    }

    private static void AppendIfPresent(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) text.AppendLine($"{label}: {value}");
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? GetStringOrList(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString())),
            _ => null
        };
    }

    private static string Humanize(string field) => field switch
    {
        "experienceRequirements" => "Experience",
        "educationRequirements" => "Education",
        _ => char.ToUpperInvariant(field[0]) + field[1..]
    };

    // La descripción suele venir como HTML, a veces escapado dos veces (&lt;p&gt;).
    private static string HtmlFragmentToText(string html)
    {
        var text = ElementToText(new HtmlParser().ParseDocument($"<body>{html}</body>").Body!);
        return text.Contains('<') && text.Contains('>') && text.Contains("</")
            ? ElementToText(new HtmlParser().ParseDocument($"<body>{text}</body>").Body!)
            : text;
    }

    // --- Fallback: the page's main content ----------------------------------------------

    private static ImportedPage FromPageContent(IDocument document, Uri url)
    {
        var title = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")
            ?? document.Title
            ?? TitleFromUrl(url);

        foreach (var noise in document.QuerySelectorAll(string.Join(',', NoiseSelectors)).ToList())
            noise.Remove();

        var root = document.QuerySelector("main, article, [role='main']") ?? document.Body;
        return new ImportedPage(title.Trim(), root is null ? null : ElementToText(root), null);
    }

    private static bool LooksLikeJobPosting(string text) =>
        PostingSignals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) >= 3;

    private static string ElementToText(IElement root)
    {
        var text = new StringBuilder();
        Walk(root, text);

        // Colapsa espacios y deja como máximo una línea en blanco seguida.
        var lines = text.ToString().Split('\n').Select(l => string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        var result = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (++blank > 1 || result.Length == 0) continue;
            }
            else blank = 0;
            result.AppendLine(line);
        }
        return result.ToString().Trim();
    }

    private static void Walk(INode node, StringBuilder text)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IText t)
            {
                text.Append(t.Data);
                continue;
            }
            if (child is not IElement element) continue;

            var isBlock = BlockTags.Contains(element.LocalName);
            if (isBlock) text.Append('\n');
            if (element.LocalName == "li") text.Append("• ");
            Walk(element, text);
            if (isBlock) text.Append('\n');
        }
    }

    private static string TitleFromUrl(Uri url)
    {
        var last = url.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(last) ? url.Host : $"{url.Host} {Uri.UnescapeDataString(last)}";
    }

    // --- Solo direcciones públicas ------------------------------------------------------
    // El servidor descarga links que manda el usuario: sin esta verificación, un link podría
    // apuntar a servicios internos (localhost, la red privada, metadata de la nube). Se valida
    // la IP real a la que se conecta, así que también cubre redirecciones y DNS engañosos.

    public static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
            throw new BlockedAddressException(context.DnsEndPoint.Host);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (first & 0xFE) == 0xFC);
        }

        var b = address.GetAddressBytes();
        return !(b[0] == 0
            || b[0] == 10
            || b[0] == 127
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // CGNAT
            || (b[0] == 169 && b[1] == 254)                // link-local / metadata de la nube
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || b[0] >= 224);                               // multicast y reservadas
    }
}

public class BlockedAddressException(string host) : IOException($"'{host}' resolves only to private or local addresses.");
