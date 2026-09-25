using System.Text;
using UglyToad.PdfPig;

namespace CareerCopilot.API.Services;

public interface IPdfTextExtractor
{
    string ExtractText(Stream pdfStream);
}

public class PdfTextExtractor : IPdfTextExtractor
{
    public string ExtractText(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);
        var text = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }

        return text.ToString();
    }
}