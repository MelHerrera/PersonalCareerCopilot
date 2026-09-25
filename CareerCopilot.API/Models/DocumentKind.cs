namespace CareerCopilot.API.Models;

// Tipo de documento: se guarda como metadata del blob y se usa para etiquetar
// el contexto que recibe el modelo ("CV" vs "Vacante").
public enum DocumentKind
{
    Other,
    Resume,
    JobPosting
}

public static class DocumentKindExtensions
{
    public static string ToLabel(this DocumentKind kind) => kind switch
    {
        DocumentKind.Resume => "CV",
        DocumentKind.JobPosting => "Vacante",
        _ => "Documento"
    };
}
