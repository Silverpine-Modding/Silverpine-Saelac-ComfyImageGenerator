using System;
using System.IO;

namespace SilverpineMods.ComfyImageGenerator;

public static class TemporaryImages
{
    public static bool IsInside(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    public static void DeleteRun(string root, string run)
    {
        if (!IsInside(root, run) || !Directory.Exists(run)) return;
        // Delete only files the plugin creates, never traverse links or nested directories.
        if ((File.GetAttributes(run) & FileAttributes.ReparsePoint) != 0) return;
        foreach (string file in Directory.GetFiles(run, "image-??.*")) File.Delete(file);
        if (Directory.GetFileSystemEntries(run).Length == 0) Directory.Delete(run);
    }
    public static void Clear(string root)
    {
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        foreach (string directory in Directory.GetDirectories(root)) DeleteRun(root, directory);
    }
    public static void SavePermanent(string root, string source, string destination, bool overwrite = false)
    {
        if (IsInside(root, destination)) throw new IOException("Choose a save location outside the plugin's temporary image folder.");
        if (!IsInside(root, source)) throw new IOException("The current image is not in the temporary image folder.");
        File.Copy(source, destination, overwrite: overwrite);
    }
}
