#!/usr/bin/env python3
"""Compile the pinned PCSX2 CSC reference and check every byte-valued input.

Only synthetic colour triples are generated. Downloads, binaries and the RGB table
go in a new directory outside Git; no PCSX2 implementation is vendored here.
"""
import argparse
import hashlib
from pathlib import Path
import subprocess
import urllib.request

REVISION = "81a20150f2b3d32dc25dbe5dfab0fac1af5d000d"
SOURCE_SHA256 = "0486fa40bff7256705418788ea6eebf27dd8d69db7896fa7b5cead0ee965bb11"
SOURCE_URL = f"https://raw.githubusercontent.com/PCSX2/pcsx2/{REVISION}/pcsx2/IPU/yuv2rgb.cpp"
REPO = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="new directory outside every Git checkout")
    parser.add_argument("--compiler", default="c++")
    args = parser.parse_args()
    output = args.output.resolve()
    if any((p / ".git").exists() for p in (output, *output.parents)):
        parser.error("output must be outside every Git checkout")
    output.mkdir(parents=True, exist_ok=False)
    source = urllib.request.urlopen(SOURCE_URL, timeout=60).read()
    if hashlib.sha256(source).hexdigest() != SOURCE_SHA256:
        raise RuntimeError("Pinned PCSX2 source hash differs")
    (output / "yuv2rgb.cpp").write_bytes(source)
    (output / "IPU").mkdir()
    (output / "Common.h").write_text("""#include <algorithm>
#include <cstdint>
using s32 = int32_t;
#define MULTI_ISA_UNSHARED_START
#define MULTI_ISA_UNSHARED_END
""")
    (output / "IPU/IPU.h").write_text("""struct macroblock_8 {
    uint8_t Y[16][16], Cb[8][8], Cr[8][8];
};
struct macroblock_rgb32 {
    struct Pixel { uint8_t r, g, b, a; } c[16][16];
};
struct Decoder { macroblock_8 mb8; macroblock_rgb32 rgb32; } decoder;
""")
    for name in ("IPU_MultiISA.h", "yuv2rgb.h"):
        (output / "IPU" / name).write_text("")
    # Do not define ARCH_X86/ARCH_ARM64: compile the actual reference function,
    # independent of either emulator's SIMD dispatch or this host's architecture.
    (output / "driver.cpp").write_text(r'''#include <cstdio>
#include <vector>
#include "yuv2rgb.cpp"
int main(int argc, char** argv) {
    if (argc != 2) return 1;
    std::vector<uint8_t> table(256 * 256 * 256 * 3);
    for (int y = 0; y < 256; ++y) decoder.mb8.Y[y / 16][y % 16] = y;
    for (int cb = 0; cb < 256; ++cb) for (int cr = 0; cr < 256; ++cr) {
        for (int i = 0; i < 64; ++i) {
            decoder.mb8.Cb[i / 8][i % 8] = cb;
            decoder.mb8.Cr[i / 8][i % 8] = cr;
        }
        yuv2rgb_reference();
        for (int y = 0; y < 256; ++y) {
            auto p = decoder.rgb32.c[y / 16][y % 16];
            int offset = ((y * 256 + cb) * 256 + cr) * 3;
            table[offset] = p.r; table[offset + 1] = p.g; table[offset + 2] = p.b;
        }
    }
    FILE* out = std::fopen(argv[1], "wb");
    if (!out) return 1;
    bool ok = std::fwrite(table.data(), 1, table.size(), out) == table.size();
    return std::fclose(out) == 0 && ok ? 0 : 1;
}
''')
    runner, table = output / "reference", output / "reference.rgb"
    subprocess.run([args.compiler, "-std=c++20", "-O2", "-I", str(output),
                    str(output / "driver.cpp"), "-o", str(runner)], check=True)
    subprocess.run([str(runner), str(table)], check=True)
    print(f"PCSX2 revision {REVISION}; source SHA-256 {SOURCE_SHA256}", flush=True)
    print(f"Synthetic RGB table SHA-256 {hashlib.sha256(table.read_bytes()).hexdigest()}", flush=True)
    command = ["dotnet", "run", "--project", str(REPO / "tools/TPW.PS2.SshScore"),
               "-c", "Release", "--", "--check-colour", str(table)]
    subprocess.run(command, check=True)
    result = subprocess.run(command + ["--perturb"])
    if result.returncode != 2:
        raise RuntimeError(f"Deliberate output mutation must fail with exit 2, got {result.returncode}")
    print("Negative control rejected as required.")


if __name__ == "__main__":
    main()
