using System.Text;

namespace TPW.PS2.Data;

/// <summary>⭐⭐ MONEY, WRITTEN THE WAY THE GAME WRITES IT -- and it is two small functions in the
/// executable, not a thing to invent.
///
/// `FUN_00142908(char* out, int value)`:
/// <code>
///   if (value &lt; 0) { value = -value; *out++ = 0x2d; }   // '-' BEFORE the symbol
///   *out = 0x24;                                         // '$'
///   FUN_00142B68(out + 1, value);
/// </code>
///
/// `FUN_00142B68(char* out, int value)` writes the digits with THOUSANDS SEPARATORS:
/// <code>
///   sprintf(tmp, "%d", value);            // the format string at 0x35F5A0 is literally "%d"
///   n = strlen(tmp);  group = n &gt; 3 ? n % 3 : 3;
///   for each digit: emit it; if (--group == 0 and not the last) emit 0x2c (',') and group = 3
/// </code>
///
/// ⚠⚠ THIS FILE PREVIOUSLY TOLD MASTER THERE WAS NO CURRENCY SYMBOL TO COPY. There is: `0x24`,
/// a dollar sign, one byte inside the formatter. The search that "proved" its absence looked for
/// STATIC STRINGS -- and a console HUD composes its text at runtime from digits, so of course
/// there were none. ⭐ A negative search over the wrong kind of evidence is not a finding, which
/// is a lesson this repo already carries and I applied to everything except this.
///
/// ⭐ The `/10` is confirmed from the display side too: `FUN_00134B98`, the finance screen,
/// fetches seven park figures and divides EVERY one by ten before formatting.
///
/// ⭐ 35 call sites share this formatter, so it is the whole UI's money, not one screen's.</summary>
public static class Money
{
    /// <summary>Park units (tenths) to the string the game would draw.</summary>
    public static string Format(int tenths) => Display(tenths / 10);

    /// <summary>The formatter itself, over an already-divided figure.</summary>
    public static string Display(int value)
    {
        var s = new StringBuilder();
        if (value < 0) { value = -value; s.Append('-'); }
        s.Append('$');
        // ⚠ The console formats the ABSOLUTE value after taking the sign off, so int.MinValue
        // would negate to itself there. Guarded rather than reproduced: a park cannot reach it,
        // and a silent wrap is not a behaviour worth being faithful to.
        string digits = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int group = digits.Length > 3 ? digits.Length % 3 : 3;
        for (int i = 0; i < digits.Length; i++)
        {
            if (--group < 0) group = 2;
            s.Append(digits[i]);
            if (group == 0 && i != digits.Length - 1) { s.Append(','); group = 3; }
        }
        return s.ToString();
    }
}
