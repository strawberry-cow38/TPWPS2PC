namespace TPW.PS2.Data;

/// <summary>Read coordinate operations from 192560/1924D0 and 191E98. This is NOT a
/// replacement for GuestWalk yet: it excludes model readiness, route-request/slot
/// ownership, execution-state dispatch and facing. Callers must supply the actual
/// active signed speed byte and native delta, not a guessed seconds conversion.</summary>
public static class NativeGuestMotion
{
    /// <summary>Signed 16-bit storage with eight fractional bits: one cell is256.</summary>
    public readonly record struct Point(short X, short Z);
    public enum Outcome { Moving, AtWaypoint, OutsideGrid }
    public readonly record struct Result(Point Position, Outcome Outcome);

    /// <summary>192560: overwrite position bits/bytes, preserving allocation bit15
    /// and the low11 link bits. Nearest quarter-cell, ties toward positive infinity.
    /// The byte cell components intentionally wrap; the reader sign-extends them.</summary>
    public static uint EncodeTarget(uint slot, Point point)
    {
        int x = point.X + 32, z = point.Z + 32;
        uint header = (slot & 0x87ffu)
            | (uint)(((x >> 6) & 3) << 11)
            | (uint)(((z >> 6) & 3) << 13);
        return header | ((uint)unchecked((byte)(x >> 8)) << 16)
            | ((uint)unchecked((byte)(z >> 8)) << 24);
    }

    /// <summary>1924D0: signed cell byte plus unsigned quarter-cell fraction.</summary>
    public static Point DecodeTarget(uint slot)
    {
        int x = unchecked((sbyte)(slot >> 16)) * 256 + (int)((slot >> 11) & 3) * 64;
        int z = unchecked((sbyte)(slot >> 24)) * 256 + (int)((slot >> 13) & 3) * 64;
        return new(unchecked((short)x), unchecked((short)z));
    }

    /// <summary>191F78..A8: signed minimum5, R5900 MULT low32, logical shift14.
    /// Delta capping belongs to1C4AA8. No per-axis fractional remainder is retained.</summary>
    public static int StepAmount(sbyte activeSpeed, int delta)
        => (int)(unchecked((uint)(Math.Max(5, (int)activeSpeed) * delta)) >> 14);

    /// <summary>Arithmetic portion of191E98, after readiness and slot decoding.
    /// Each axis independently clamps its displacement (no diagonal normalization).
    /// Outside-grid candidates are rejected before committing either coordinate.
    /// AtWaypoint is NOT route completion: the native slot-release/no-slot/mode
    /// dispatch still runs in later guest updates. No callbacks are invoked here.</summary>
    public static Result AdvanceCoordinates(Point current, Point target, sbyte activeSpeed,
        int delta, int columns, int rows)
    {
        int step = StepAmount(activeSpeed, delta);
        var next = new Point(
            unchecked((short)(current.X + Math.Clamp(target.X - current.X, -step, step))),
            unchecked((short)(current.Z + Math.Clamp(target.Z - current.Z, -step, step))));
        int x = next.X >> 8, z = next.Z >> 8;
        if (x < 0 || z < 0 || x >= columns || z >= rows)
            return new(current, Outcome.OutsideGrid);
        return new(next, next == target ? Outcome.AtWaypoint : Outcome.Moving);
    }
}