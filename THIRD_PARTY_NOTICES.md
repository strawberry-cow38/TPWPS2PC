# FFmpeg-derived managed IPU decoder

`core/TPW.PS2.Data/IpuDecoder.cs` and `IpuTables.cs` adapt MPEG-1 tables,
intra-block reconstruction, and the integer simple IDCT from FFmpeg **n7.0.2**:

- [IPU frame syntax](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/mpeg12dec.c)
- [MPEG-1 intra reconstruction](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/mpeg12.c)
- [VLC and quantiser tables](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/mpeg12data.c)
- [Simple IDCT](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/simple_idct_template.c)
- [AArch64 IDCT arithmetic](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/aarch64/simple_idct_neon.S)

Copyright (c) 2000, 2001 Fabrice Bellard; 2001-2004 Michael Niedermayer;
2008 Mans Rullgard; 2017 Matthieu Bouron. These adaptations are distributed
under the GNU Lesser General Public License, version 2.1 or later; see
[the full license](licenses/LGPL-2.1.txt). The modifications translate the supported
IPU subset to managed C#, validate bounded bit reads, remove transport and native
dispatch, and reproduce the reference AArch64 integer operations with scalars.
There is no FFmpeg executable or native-library dependency in the resulting decoder.
