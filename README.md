# CareerCopilot

Asistente de búsqueda de empleo con RAG sobre tu CV y descripciones de vacantes,
construido para ir integrando las capacidades de Azure AI Foundry en curso de codigo facilito + Microsoft.

## Qué hace ahora mismo

1. Subes un PDF (tu CV o una descripción de vacante) → se guarda en Blob Storage.
2. Se extrae el texto del PDF, se trocea en fragmentos y se generan embeddings con
   un modelo de Azure AI Foundry.
3. Le preguntas cosas como *"¿qué tan bien encajo con esta vacante?"* y el asistente
   responde usando esos fragmentos como contexto (RAG).

## Prerrequisitos

- .NET 10 SDK
- Un recurso de Azure AI Foundry (o Azure OpenAI) con **dos deployments**:
  - Un modelo de chat, ej. `gpt-4o-mini`
  - Un modelo de embeddings, ej. `text-embedding-3-small`
- Una cuenta de Azure Storage, o el emulador local [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
  (`UseDevelopmentStorage=true` ya viene configurado por defecto para eso)

## Configuración

Completa `appsettings.json` con tu endpoint, API key y nombres de deployment.

Para no dejar la API key en el archivo (mejor práctica, sobre todo si esto termina
en un repo), puedes usar `dotnet user-secrets` en su lugar:

```bash
dotnet user-secrets init
dotnet user-secrets set "AzureOpenAI:ApiKey" "TU-API-KEY"
```

## Ejecutar

```bash
dotnet restore
dotnet run
```

## Probar los endpoints

Subir un documento:

```bash
curl -X POST http://localhost:5000/documents \
  -F "file=@/ruta/a/tu-cv.pdf"
```

Listar documentos subidos:

```bash
curl http://localhost:5000/documents
```

Preguntar:

```bash
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{"question": "¿Qué tan bien encaja mi CV con la vacante que subí?"}'
```

## Cómo sigue creciendo

- **Azure AI Search**: reemplaza `InMemoryVectorStore` por una implementación de
  `IVectorStore` respaldada por Azure AI Search — el resto de la app no cambia.
- **Function calling**: dale al modelo herramientas (calcular un match score,
  extraer requisitos en JSON estructurado).
- **Agentes**: automatiza el flujo completo (leer vacante → comparar con CV →
  redactar carta de presentación → guardarla en Blob Storage).
- **Observabilidad**: instrumenta con Application Insights y despliega como
  Azure Function o App Service.
