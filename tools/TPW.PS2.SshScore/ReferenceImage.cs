using System.Diagnostics;
using TPW.PS2.Data;

internal static class ReferenceImage
{
    public static Targa Read(string path)
    {
        var data = File.ReadAllBytes(path);
        // One of the 400 supplied .tga partners is actually a PNG. Detect content, not extension;
        // retain it in the same scored population. Use the existing PNG decoder, not a new one.
        if (!data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return new Targa(data);
        return ReadPng(path).GetAwaiter().GetResult();
    }

    static async Task<Targa> ReadPng(string path)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("TPW_FFMPEG") ?? "ffmpeg")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-v", "error", "-xerror", "-threads", "1", "-i", Path.GetFullPath(path),
            "-frames:v", "1", "-f", "image2pipe", "-c:v", "targa", "-pix_fmt", "bgra", "-threads", "1", "pipe:1" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var output = new MemoryStream();
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token), process.WaitForExitAsync(timeout.Token));
            string error = await errors;
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error)) throw new InvalidDataException($"PNG reference decode failed: {error}");
            return new Targa(output.ToArray());
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
