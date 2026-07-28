using System.IO;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class AtomicFile {
    public static void Write(string path, Action<string> writeTemporaryFile) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeTemporaryFile);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException("The destination path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try {
            writeTemporaryFile(temporaryPath);
            File.Move(temporaryPath, fullPath, overwrite: true);
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void WriteAllText(string path, string contents, Encoding? encoding = null) {
        Write(path, temporaryPath => File.WriteAllText(
            temporaryPath,
            contents,
            encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
    }
}
