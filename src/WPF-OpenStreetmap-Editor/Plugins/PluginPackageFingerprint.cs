using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Plugins;

public static class PluginPackageFingerprint {
    public static string Compute(string packageDirectory) {
        var packageRoot = Path.GetFullPath(packageDirectory);
        if (!Directory.Exists(packageRoot)) {
            throw new DirectoryNotFoundException(packageRoot);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = PluginPackageFiles
            .Enumerate(packageRoot)
            .Select(path => new {
                FullPath = path,
                RelativePath = Path.GetRelativePath(packageRoot, path).Replace('\\', '/')
            })
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal);

        foreach (var file in files) {
            var relativePathBytes = Encoding.UTF8.GetBytes(file.RelativePath);
            AppendLength(hash, relativePathBytes.Length);
            hash.AppendData(relativePathBytes);
            using var stream = File.OpenRead(file.FullPath);
            AppendLength(hash, stream.Length);
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0) {
                hash.AppendData(buffer.AsSpan(0, bytesRead));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendLength(IncrementalHash hash, long length) {
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, length);
        hash.AppendData(lengthBytes);
    }
}
