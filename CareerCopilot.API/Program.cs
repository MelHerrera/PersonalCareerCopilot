using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
using CareerCopilot.API.Endpoints;
using CareerCopilot.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Un único cliente de Azure OpenAI compartido por chat y embeddings.
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["AzureOpenAI:Endpoint"]
        ?? throw new InvalidOperationException("Falta AzureOpenAI:Endpoint.");
    var apiKey = config["AzureOpenAI:ApiKey"]
        ?? throw new InvalidOperationException("Falta AzureOpenAI:ApiKey.");
    return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
});

builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
builder.Services.AddSingleton<IVectorStore, InMemoryVectorStore>();
builder.Services.AddSingleton<ICareerChatService, CareerChatService>();
builder.Services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();

// Cliente para importar vacantes desde un link: solo se conecta a direcciones públicas.
builder.Services.AddHttpClient<IWebPageImporter, WebPageImporter>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/pdf;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en,es;q=0.8");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectCallback = WebPageImporter.ConnectToPublicAddressAsync,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        MaxAutomaticRedirections = 5
    });

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

// Solo hace falta si el front se sirve desde otro origen sin el proxy de Angular.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

// En producción, el build de CareerCopilot.Web se publica en wwwroot y se sirve desde aquí.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSystemEndpoints();
app.MapDocumentEndpoints();
app.MapChatEndpoints();

// Rutas del SPA (ej. /chat) que no son archivos ni endpoints de la API.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
