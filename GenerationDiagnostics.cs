using System;
using System.Text;

namespace SilverpineMods.ComfyImageGenerator;

public static class GenerationDiagnostics
{
    public static string ForLog(Exception exception)
    {
        var text = new StringBuilder();
        Exception? current = exception;
        for (int depth = 0; current != null && depth < 8; depth++, current = current.InnerException)
        {
            if (depth > 0) text.Append("\nCaused by: ");
            text.Append(current.GetType().FullName);
            if (!string.IsNullOrEmpty(current.StackTrace)) text.Append('\n').Append(current.StackTrace);
        }
        return text.ToString();
    }
}
