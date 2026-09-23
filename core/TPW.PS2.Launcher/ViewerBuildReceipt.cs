using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace TPW.PS2.Launcher;

/// <summary>Positive evidence of a successful build, separate from the checkout.
/// A DLL can exist after a failed build, or survive a failed deletion. Neither
/// means it is safe to launch. Missing/malformed receipts always require a build.
/// This is local build provenance, not a signature or an authenticity guarantee.</summary>
public sealed class ViewerBuildReceipt(string receiptPath)
{
    sealed record Receipt(int Schema, string Revision, string Sha256);
    sealed record Artifact(string Path, string Sha256);
    static bool RevisionValid(string revision) => revision != null
        && revision.Length is 40 or 64 && revision.All(Uri.IsHexDigit);
    static string Hash(string assembly)
    {
        using var input = File.OpenRead(assembly);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    // Include the managed output's runtime closure, not just the viewer DLL.
    // Stable relative paths mean added/removed/renamed files change the manifest.
    // Engine installation, external plugins and native non-DLL files are outside
    // this local managed-build receipt; it is not whole-machine attestation.
    static string ManifestHash(string assembly)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(assembly));
        if (!File.Exists(assembly)) throw new FileNotFoundException("Viewer assembly is missing", assembly);
        bool RuntimeFile(string path) => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
        var artifacts = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(RuntimeFile).Select(path => new Artifact(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(path)))
            .OrderBy(a => a.Path, StringComparer.Ordinal).ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(artifacts))));
    }

    /// <summary>Must succeed BEFORE changing the checkout or build artifacts.
    /// Exceptions intentionally propagate: an update without invalidation is unsafe.</summary>
    public void Invalidate() => File.Delete(receiptPath);

    public bool CanLaunch(string revision, string assembly)
    {
        if (!RevisionValid(revision)) return false;
        try
        {
            if (!File.Exists(assembly) || !File.Exists(receiptPath) || new FileInfo(receiptPath).Length > 4096) return false;
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(receiptPath));
            return receipt is { Schema: 2 } && string.Equals(receipt.Revision, revision, StringComparison.OrdinalIgnoreCase)
                && string.Equals(receipt.Sha256, ManifestHash(assembly), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    /// <summary>Only call after a zero build exit and artifact existence check.
    /// An interrupted receipt write is rejected on the next startup.</summary>
    public void RecordSuccess(string revision, string assembly)
    {
        if (!RevisionValid(revision)) throw new ArgumentException("Cannot record a build without a full Git revision", nameof(revision));
        var receipt = new Receipt(2, revision, ManifestHash(assembly));
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt));
    }
}
