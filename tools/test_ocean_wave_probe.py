"""Asset-free guards for the read-only native-water evidence probe."""
import struct
import unittest

from ocean_wave_probe import ElfImage, require_word, source_alpha


def synthetic_elf():
    data = bytearray(0xC0)
    data[:6] = b"\x7fELF\x01\x01"
    struct.pack_into("<I", data, 28, 52)
    struct.pack_into("<HH", data, 42, 32, 1)
    struct.pack_into("<8I", data, 52, 1, 0x80, 0x100000, 0x100000, 0x40, 0x60, 5, 16)
    struct.pack_into("<I", data, 0x80, 0x22CB90)
    return data


class OceanWaveProbeTests(unittest.TestCase):
    def test_maps_virtual_address_through_program_header(self):
        self.assertEqual(ElfImage(synthetic_elf()).word(0x100000), 0x22CB90)

    def test_does_not_treat_bss_as_file_bytes(self):
        with self.assertRaisesRegex(ValueError, "not file-backed"):
            ElfImage(synthetic_elf()).at(0x100040, 4)

    def test_rejects_wrong_address_frame(self):
        with self.assertRaisesRegex(ValueError, "not file-backed"):
            ElfImage(synthetic_elf()).word(0)

    def test_rejects_invalid_program_header_extent(self):
        data = synthetic_elf()
        struct.pack_into("<I", data, 52 + 16, 0x1000)
        with self.assertRaisesRegex(ValueError, "invalid PT_LOAD"):
            ElfImage(data)

    def test_actual_word_not_name_proximity_is_required(self):
        data = synthetic_elf()
        struct.pack_into("<I", data, 0x80, 0x22CB30)
        with self.assertRaisesRegex(ValueError, "native witness"):
            require_word(ElfImage(data), 0x100000, 0x22CB90)

    def test_alpha_statistics_honor_tga_id_length(self):
        data = bytearray(20)
        data[0], data[2], data[16] = 2, 2, 32
        struct.pack_into("<HH", data, 12, 2, 1)
        data.extend(bytes((0, 0, 0, 94, 0, 0, 0, 154)))
        self.assertEqual(source_alpha(data),
                         {"width": 2, "height": 1, "count": 2, "min": 94, "max": 154, "mean": 124})

    def test_rejects_truncated_pixels(self):
        data = bytearray(18)
        data[2], data[16] = 2, 32
        struct.pack_into("<HH", data, 12, 1, 1)
        with self.assertRaisesRegex(ValueError, "truncated"):
            source_alpha(data)

    def test_rgb_only_is_not_alpha_evidence(self):
        data = bytearray(21)
        data[2], data[16] = 2, 24
        struct.pack_into("<HH", data, 12, 1, 1)
        with self.assertRaisesRegex(ValueError, "32bpp"):
            source_alpha(data)


if __name__ == "__main__":
    unittest.main()