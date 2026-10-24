using System;
using System.IO;
using System.Linq;

namespace SilverpineMods.ComfyImageGenerator;

// The save form uses GeneratorWindow's dark IMGUI skin. Keep path handling
// independent of Unity and avoid native common-dialog interop.
internal static class SaveImageDialog
{
    internal static string DefaultFolder()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures)) pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(pictures) ? "" : Path.Combine(pictures, "Silverpine");
    }
    internal static string DefaultName(string extension) => "Silverpine-scene-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + extension;

    internal static string Destination(string folder, string name, string extension, string temporaryRoot)
    {
        extension = extension.ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg") throw new ArgumentException("Save As supports PNG and JPEG images.");
        folder = folder.Trim(); name = name.Trim();
        if (folder.Length == 0 || !Path.IsPathRooted(folder) || !Path.IsPathFullyQualified(folder))
            throw new ArgumentException("Enter an absolute folder path, such as C:\\Users\\your-name\\Pictures.");
        if (name.Length == 0 || name.Length > 200 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("/") || name.Contains("\\") || name.EndsWith("."))
            throw new ArgumentException("Enter a file name of 1-200 characters without path separators, invalid characters or a trailing dot.");
        string device = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(device))
            throw new ArgumentException("That file name is reserved by Windows.");
        if (Path.GetExtension(name).Length == 0) name += extension;
        if (!string.Equals(Path.GetExtension(name), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use the image's " + extension + " extension, or enter a name without an extension.");
        string destination = Path.GetFullPath(Path.Combine(folder, name));
        if (TemporaryImages.IsInside(temporaryRoot, destination)) throw new IOException("Choose a permanent folder outside TemporaryImages.");
        return destination;
    }
}
