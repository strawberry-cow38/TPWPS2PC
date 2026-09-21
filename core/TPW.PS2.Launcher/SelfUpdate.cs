using System.Security.Cryptography;

namespace TPW.PS2.Launcher;

/// <summary>The checks that stand between a download and overwriting the running launcher.
///
/// ⚠⚠ THE FAILURE THIS GUARDS IS BRICKING THE USER. A self-update writes over the only copy of the
/// program they have. If what arrives is a truncated download, a GitHub error page, a redirect body
/// or a half-written file, and it is copied over the exe anyway, the launcher is gone and there is
/// no launcher left with which to fix it. There is no recovery path from inside the thing that
/// broke.
///
/// So this refuses by default and accepts only on positive evidence -- and it lives in core, where
/// a test can reach it, because it is the last check before an irreversible act and it is exactly
/// the sort of code that never runs during development.</summary>
public static class SelfUpdate
{
    /// <summary>A framework-dependent single-file Avalonia launcher is a few MB. Anything
    /// dramatically smaller is an error page or a partial transfer, not a program.
    ///
    /// ⚠ Deliberately an order of magnitude below the real size rather than just under it: this is
    /// a sanity FLOOR, not a size assertion, and a tight bound would reject a legitimately smaller
    /// future build.</summary>
    public const int MinPlausibleBytes = 200_000;

    /// <summary>Does this look like a Windows executable at all? PE files begin "MZ".
    ///
    /// ⚠ Cheap, and it catches the common disaster: an HTML error body starts "&lt;!DOCTYPE" or
    /// "&lt;html", never "MZ". A 404 page is a perfectly successful HTTP response, so nothing
    /// upstream of here will have complained about it.</summary>
    public static bool LooksLikeWindowsExe(byte[] bytes, int minBytes = MinPlausibleBytes) =>
        bytes != null && bytes.Length >= minBytes && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z';

    public static string Sha256Of(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    public readonly record struct Verdict(bool Accept, string Reason);

    /// <summary>Should these bytes be allowed to replace the running launcher?
    ///
    /// ⚠ AN ABSENT EXPECTED HASH IS NOT A PASS AND NOT A FAIL. If the publisher did not upload one
    /// we can verify plausibility but not identity -- so this accepts on the shape checks and SAYS
    /// the stronger check did not run. Silently treating "no hash published" as "hash verified" is
    /// how a verification step becomes decorative. The caller logs the reason either way, so a
    /// launcher that has stopped hash-checking says so in its own log rather than going quiet.</summary>
    public static Verdict Check(byte[] bytes, string expectedSha256)
    {
        if (bytes == null || bytes.Length == 0)
            return new(false, "download was empty");
        if (!LooksLikeWindowsExe(bytes))
            return new(false, $"got {bytes.Length:n0} bytes and it is not a Windows executable "
                            + "(an error page or a partial transfer) — keeping the current launcher");
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return new(true, "shape looks right; no published hash to verify against");

        string actual = Sha256Of(bytes);
        if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            return new(false, $"hash mismatch — published {expectedSha256.Trim()[..16]}…, "
                            + $"downloaded {actual[..16]}… — keeping the current launcher");
        return new(true, "hash matches the published build");
    }

    /// <summary>Is <paramref name="published"/> strictly newer than <paramref name="running"/>?
    ///
    /// ⚠ UNPARSEABLE MEANS NO. A 404 page read as a version string must never be able to start a
    /// self-replacement, so anything that is not a plain integer is treated as "nothing to do"
    /// rather than as an error to surface -- and certainly not as an upgrade.</summary>
    public static bool ShouldSelfUpdate(int running, string publishedRaw) =>
        int.TryParse((publishedRaw ?? "").Trim(), out var published) && published > running;
}
