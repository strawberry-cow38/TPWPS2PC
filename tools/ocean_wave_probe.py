"""Read-only PS2 procedural-water evidence probe; never extracts game assets.

Usage: python3 tools/ocean_wave_probe.py --disc /path/to/tpw_ps2.bin
This checks file-backed native bindings and data placement, not native execution.
"""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import struct

import iso2
import m3d2
import ssh
import wadtree

ELF_SHA = "231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a"
WORLD_NAMES = ("JUNGLE", "HALLOW", "FANTASY", "SPACE")


def u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


class ElfImage:
    """Map only file-backed PT_LOAD ranges; do not treat BSS as image data."""

    def __init__(self, data):
        if len(data) < 52 or data[:6] != b"\x7fELF\x01\x01":
            raise ValueError("expected little-endian ELF32")
        self.data = data
        phoff = u32(data, 28)
        entry_size, count = struct.unpack_from("<HH", data, 42)
        if entry_size < 32 or phoff + count * entry_size > len(data):
            raise ValueError("invalid program-header table")
        self.loads = []
        for index in range(count):
            header = struct.unpack_from("<8I", data, phoff + index * entry_size)
            kind, offset, address, _, size, memory_size, _, _ = header
            if kind == 1:
                if size > memory_size or offset + size > len(data):
                    raise ValueError("invalid PT_LOAD extent")
                self.loads.append((address, offset, size))

    def at(self, address, size):
        if size < 0:
            raise ValueError("negative read size")
        for start, offset, extent in self.loads:
            if start <= address and address + size <= start + extent:
                begin = offset + address - start
                return self.data[begin:begin + size]
        raise ValueError(f"not file-backed: {address:#x}+{size:#x}")

    def word(self, address):
        return u32(self.at(address, 4), 0)

    def text(self, address, limit=64):
        return self.at(address, limit).split(b"\0", 1)[0].decode("ascii")


def require_word(image, address, expected):
    actual = image.word(address)
    if actual != expected:
        raise ValueError(f"native witness {address:#x}: {actual:#x} != {expected:#x}")
    return {"address": hex(address), "word": hex(actual)}


def archive_member(archive, name):
    matches = [entry for entry in wadtree.walk(archive)
               if entry[0].casefold() == name.casefold()]
    if len(matches) != 1:
        raise ValueError(f"expected one archive member {name}; found {len(matches)}")
    _, offset, stored, decompressed = matches[0]
    return wadtree.read(archive, offset, stored, decompressed)


def source_alpha(tga):
    if len(tga) < 18 or tga[1] != 0 or tga[2] != 2 or tga[16] != 32:
        raise ValueError("expected raw true-colour 32bpp TGA counterpart")
    width, height = struct.unpack_from("<HH", tga, 12)
    start = 18 + tga[0]
    end = start + width * height * 4
    if not width or not height or end > len(tga):
        raise ValueError("truncated/empty TGA")
    values = Counter(tga[start + 3:end:4])
    return {"width": width, "height": height, "count": sum(values.values()),
            "min": min(values), "max": max(values),
            "mean": sum(value * count for value, count in values.items()) / (width * height)}


def sea_bounds(model):
    transforms = m3d2.world_transforms(model)
    table = u32(model, 0x48)
    out = []
    for index, mesh in enumerate(m3d2.meshes(model)):
        if not mesh["name"].startswith("A_SEA_"):
            continue
        matrix = transforms[table + index * 160]
        vertices = [m3d2.xform(matrix, vertex)
                    for strip in m3d2.strips(model, mesh) for vertex in strip]
        if not vertices or not all(math.isfinite(value) for vertex in vertices for value in vertex):
            raise ValueError(f"missing/nonfinite sea geometry: {mesh['name']}")
        out.append({"mesh": mesh["name"], "vertex_count": len(vertices),
                    "bounds": [[min(vertex[axis] for vertex in vertices),
                                max(vertex[axis] for vertex in vertices)] for axis in range(3)]})
    if len(out) != 6:
        raise ValueError(f"expected six sea meshes; found {len(out)}")
    return out


def probe(disc):
    with Path(disc).open("rb") as stream:
        pvd = iso2.sector(stream, 16)
        if pvd[1:6] != b"CD001":
            raise ValueError("expected Mode2/2352 ISO image")
        root = pvd[156:190]
        files = []
        iso2.walk(stream, u32(root, 2), u32(root, 10), "", files)

        def read_file(name):
            matches = [entry for entry in files if entry[0].casefold() == name.casefold()]
            if len(matches) != 1:
                raise ValueError(f"expected one ISO file {name}; found {len(matches)}")
            _, sector, size = matches[0]
            return b"".join(iso2.sector(stream, sector + index)
                            for index in range((size + 2047) // 2048))[:size]

        elf_data = read_file("/SLES_500.32")
        digest = hashlib.sha256(elf_data).hexdigest()
        if digest != ELF_SHA:
            raise ValueError("unsupported executable; native anchors need rereading")
        image = ElfImage(elf_data)
        witnesses = [require_word(image, address, expected) for address, expected in (
            (0x36F6FC, 0x0022CB90),  # actual virtual render callback
            (0x149B8C, 0x0C0883E8), (0x149BBC, 0x0C0883E8),  # park calls water constructor
            (0x2210C0, 0x0C08D46A),  # texture-loader call
            (0x22CC0C, 0x0C08B2CC),  # per-render animation advance
            (0x22CDC8, 0x0C08E4D6),  # cubic sampler call
            (0x22CCD8, 0x2406005C), (0x22CCF0, 0x0C08AD1E),  # PRIM and packet builder
            (0x22D070, 0xE4810004),  # generated Y written into vertex packet
            (0x22ED34, 0x0C08A81A),  # packet queued with texture
        )]
        directory, texture = image.text(0x36E7E8), image.text(0x36E8B0)
        if (directory, texture) != ("data/generic/extra/", "justwater.ssh"):
            raise ValueError("water texture binding changed")
        common = read_file("/DATA/DATA.WAD")
        texture_meta = list(ssh.entries(archive_member(common, "/Generic/extra/justwater.ssh")))
        if len(texture_meta) != 1 or (texture_meta[0]["type"], texture_meta[0]["width"],
                                      texture_meta[0]["height"]) != (5, 64, 64):
            raise ValueError("unexpected justwater SHPS format")
        alpha = source_alpha(archive_member(common, "/Generic/extra/justwater.tga"))
        terrains = []
        for world in WORLD_NAMES:
            archive = read_file(f"/DATA/{world}.WAD")
            for variant in (1, 2):
                bounds = sea_bounds(archive_member(archive, f"/Terrain/terrain_{variant}.mps"))
                if any(mesh["bounds"][1][0] < -1.11 or mesh["bounds"][1][1] > -0.99
                       for mesh in bounds):
                    raise ValueError(f"unexpected composed sea Y envelope: {world}/{variant}")
                terrains.append({"world": world, "terrain": variant, "sea": bounds})
        return {"executable_sha256": digest, "native_witnesses": witnesses,
                "texture_directory": directory, "texture_name": texture,
                "shps": texture_meta, "tga_source_alpha": alpha, "terrains": terrains,
                "native_execution": False, "assets_written": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--disc", required=True)
    arguments = parser.parse_args()
    print(json.dumps(probe(arguments.disc), indent=2))