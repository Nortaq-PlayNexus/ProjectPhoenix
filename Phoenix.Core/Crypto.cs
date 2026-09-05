using System.Security.Cryptography;
using System.Text;

namespace Phoenix.Core;

public static class Crypto
{
    public static string Sha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Sha256(bytes);
    }

    public static string Sha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string Sha256Directory(string root)
    {
        var sb = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(file).StartsWith("FORENSIC_MANIFEST", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.Append(Sha256File(file));
        }
        return Sha256(sb.ToString());
    }

    public static string Blakes3(byte[] bytes)
    {
        // BLAKE3 is the preferred accelerator for internal deduplication. When a
        // native/managed BLAKE3 provider is registered it should be invoked here;
        // until then we fall back to SHA-256 so the pipeline never breaks.
        return Sha256(bytes);
    }
}
