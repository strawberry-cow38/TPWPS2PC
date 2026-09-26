# The dynamic sky: clouds, weather greying, and what the port is missing

Master: *"clouds ARE meant to shift; sky gets gray when raining. and also lighting."* All three
are one frame function apart, and the port does none of them.

## WHAT

1. **Two cloud layers drift**, at a fixed rate, in every world. The far one crosses the texture in
   about 2.5 minutes; the near one moves at **exactly twice** that.
2. **Weather darkens the scene's two light colours** on an eased curve. This is already decoded in
   `findings/lighting.md` and already implemented in the port -- but it never fires, because the
   port pins the weather amount at zero.
3. **Nothing tints the sky specifically.** The sky tick and the weather tick are siblings in the
   same frame function and the weather object is never handed to the sky.

## HOW

### The frame loop

`0x225fc8` is the per-frame driver, and it is the whole story in six calls:

```c
FUN_00220c78();                  // clock
if (obj+0x350) {
  FUN_00232328(0x331570);        // sky tick -- scrolls the clouds
  FUN_0023efe8(0x342e28, dt);    // weather tick -- darkens ambient + directional
  FUN_0021ea20();                // builds texture matrices (consumes the 2x cloud offset)
  FUN_00225bf8(obj, ...);        // draw
  FUN_0022a378(0x311160);        // display-list reset
  FUN_002265b8(obj);
}
```

⭐ The sky object is the global **`0x331570`**, created by `0x149958` through
`FUN_00231908(sky, "Data/<World>/Sky/", "<World>_back.tga", "<World>_front2.tga")`.

### Clouds -- `0x232328`, called once a frame

```c
dt = DAT_002f07b0;                          // ms since last frame
if (dt > 1000) dt = DAT_00310cbc * 0x12;    // a long stall uses a substitute, not the real gap
vx = sky[+0x00] * dt/1000;                  // X rate
vy = sky[+0x04] * dt/1000;                  // Y rate
sky[+0x08] += vx;  sky[+0x0c] += vy;        // FAR layer UV, wrapped into 0..1
DAT_002f0270 += 2*vx;  DAT_002f0274 += 2*vy; // NEAR layer UV, also wrapped, at DOUBLE rate
```

⚠⚠ **CORRECTION TO THE FIRST VERSION OF THIS FILE.** I wrote that the rates "are set once,
globally, the same four floats for every world". They are not. `0x231848` sets *initial* values --
X `0.0067`, Y `0.0037`, starting U `0.1`, V `0.2` -- and **the weather tick overwrites them every
frame**. I had found the initialiser and stopped, which is the same mistake as reading a field's
default and calling it the value.

### The clouds blow in a WIND DIRECTION, and the weather turns it

In `0x23efe8`, before the greying:

```c
i  = (uint)(angle * 40.743664);            // 40.743664 = 256 / 2pi -> a 256-entry sine table
c  = sinTable[(i + 0x40) & 0xff];          // +0x40 is +90 degrees, so this is the cosine
s  = sinTable[ i        & 0xff];
FUN_00232318(-c * 0.01, s * 0.01, sky);    // writes sky[+0x00], sky[+0x04] -- the scroll rate
```

So the scroll vector is a **unit wind direction times 0.01 UV/second**, and the angle advances
over time: the clouds change heading. Magnitude is always 0.01, which is not the magnitude of the
initial pair (0.00765) -- another sign those four floats are defaults and nothing more. The same
`c`/`s` also feed `0x342e00`/`0x342e08`, scaled by `0.05 * 0.2`.

The second pair is consumed by `0x21ea20`, which folds it into texture matrices at `0x2f0540`.

### Weather greying -- `0x23efe8`, called once a frame

Already in `findings/lighting.md`; restated because it is half of master's question. Weather
object `0x342e28`:

```text
t = 0.8 * weather_amount
f = 2t - t*t
ambient     = (0.5,0.5,0.5) - (0.13,0.13,0.13) * f
directional = (0.6,0.6,0.6) - (0.10,0.10,0.10) * f
```
At full weather that is ambient `0.3752` and directional `0.504`.

## WHY THE PORT SHOWS NEITHER

- **Clouds:** `SkyDome` has no animation at all. The comment already said "NOT DONE: how fast the
  clouds move" -- this file is the answer to that.
- **Greying:** the lighting path IS implemented, but `DefaultWeatherAmount = 0` "until the actual
  weather state machine is ported", so `f` is always 0 and the clear colours are always used. The
  formula has never had a nonzero input.

## The sky greying: a PALETTE rewrite, driven by the same curve as the light

⭐⭐ **RESOLVED, and it is neither of the two guesses.** Not fog, and not "the sky is lit geometry".
The weather tick recolours the sky's **palette** on the CPU and re-uploads the texture.

`0x23efe8` computes the eased factor once and hands it to BOTH consumers:

```c
f = 2t - t*t                               // the SAME curve the light colours use
DAT_00311160..0x311178 = ambient/directional reduced by f    // the lighting path
FUN_00232700(f, 0x331570);                 // and the SKY
DAT_002f02bc = (1 - f) * 0.3 + 0.2;
```

`0x232700` walks **0x400 bytes = 256 RGBA entries** -- a palette, not an image -- from the pristine
copy at `sky+0x22c`, and for each entry:

```c
a   = src.A / 127.0
mul = (1 - f) + f * (a * 0.2 + 0.3)        // clamped to 0..1
dst.RGB = src.RGB * mul
dst.A   = ((1 - f) * a + f * (1 - 0.15 * a)) * 127
```
then `FUN_002349e8(tex, 1)` re-uploads it.

At full weather an opaque cloud texel keeps `0.5` of its colour and a clear-sky texel only `0.3`
-- **the sky darkens harder than the clouds do**, which is what turns a blue sky with white clouds
into flat overcast. Alpha moves the other way: a fully clear texel goes to `1.0`, so the cloud
sheet thickens and covers.

## ⚠ OPEN

- **Where the weather amount comes from.** The curve and both consumers are read; the state machine
  that drives `amount` is not, and the port pins it at 0.
- **What `DAT_002f02bc` ((1-f)*0.3 + 0.2) and the `0x342e00/08` pair are for.** Both are written
  from the same weather values and neither consumer has been read.
- ⚠ The port's sky is a `shader_type sky` with no palette: reproducing this needs the two sky
  textures kept as indexed data, or the same multiply applied in the shader. The second is easier
  and is NOT the same thing -- a palette multiply happens once per colour, a shader multiply once
  per pixel, and they only agree because the operation is linear. Worth writing down before
  someone calls them equivalent.
