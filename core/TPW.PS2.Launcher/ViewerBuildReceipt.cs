using System.Security.Cryptography;
using System.Text.Json;

namespace TPW.PS2.Launcher;

/// <summary>Positive evidence of a successful build, separate from the checkout.
/// A DLL can exist after a failed build, or survive a failed deletion. Neither
/// means it is safe to launch. Missing/malformed receipts always require a build.
/// This is local build provenance, not a signature or an authenticity guarantee.</summary>
public sealed class ViewerBuildReceipt(string receiptPath)
{
    sealed record Receipt(int Schema, string Revision, string Sha256);
    static bool RevisionValid(string revision) => revision != null
        && revision.Length is 40 or 64 && revision.All(Uri.IsHexDigit);
    static string Hash(string assembly)
    {
        using var input = File.OpenRead(assembly);
        return Convert.ToHexString(SHA256.HashData(input));
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
            return receipt is { Schema: 1 } && string.Equals(receipt.Revision, revision, StringComparison.OrdinalIgnoreCase)
                && string.Equals(receipt.Sha256, Hash(assembly), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    /// <summary>Only call after a zero build exit and artifact existence check.
    /// An interrupted receipt write is rejected on the next startup.</summary>
    public void RecordSuccess(string revision, string assembly)
    {
        if (!RevisionValid(revision)) throw new ArgumentException("Cannot record a build without a full Git revision", nameof(revision));
        var receipt = new Receipt(1, revision, Hash(assembly));
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt));
    }
}
