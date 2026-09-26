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

The rates are set once, globally, by `0x231848` -- the same four floats for every world:

| Field | Raw | Value | Meaning |
|---|---|---|---|
| `+0x00` | `0x3bdb8bac` | **0.0067** | X scroll, UV per second |
| `+0x04` | `0x3b727bb3` | **0.0037** | Y scroll, UV per second |
| `+0x08` | `0x3dcccccd` | 0.1 | starting U |
| `+0x0c` | `0x3e4ccccd` | 0.2 | starting V |

So the far layer takes **149 s** to wrap in U and **270 s** in V; the near layer **75 s** and
**135 s**. Slow drift, not visible motion -- which is why nobody noticed it was missing.
⭐ The values are round in decimal (0.0067, 0.0037, 0.1, 0.2), i.e. hand-authored, which is a
reasonable check that they are being read as the right type at the right offsets.

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

## ⚠ OPEN

**Whether the sky itself greys, or only the things under it.** `0x225fc8` shows the weather object
is never passed to the sky, so there is no sky-specific tint on this path. The likely explanation
is that the sky dome is ordinary geometry going through the same per-vertex ambient+directional
path as everything else, so darkening those two colours greys it along with the park -- one
mechanism, not two. **That is a hypothesis and is not established here**; it needs the sky dome's
own draw path read. ⚠ It matters for the port, because the port's sky is a `shader_type sky` that
writes COLOR directly and is lit by nothing: if the console's sky is lit geometry, ours will stay
bright while the park goes grey, and that is exactly the symptom master would report next.
