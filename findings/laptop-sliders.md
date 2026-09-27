# What the laptop's sliders actually do

Master, 2026-09-27: *"wire the sliders to work and modify the excitement, reliability etc etc etc
exactly how the game does it. no guessing, do real research."* This is the research. It is written
before any wiring, and it says plainly where the reading stops.

## The shops screen -- already decoded, three controls

`0x1D7070` is the shop management screen writing back, and `ParkSim` already carries the reading:

| control | write | field | clamp |
|---|---|---|---|
| quality slider | `FUN_001D1F58(shop, ui[0xa82])` | `+0xBA` Quality | 0..100 |
| the other slider | `FUN_001D1FC0(shop, ui[0xbba])` | `+0xAC` | 0..100 |
| cost of item | `shop[0xb8] = ui[0xd2c]` | `+0xB8` price | **1..500** |

The clamps are the screen's own setup, `0x1D6D28`. ⭐ And what they DO is one expression, used for
both the guest's want and the cost of goods:

> `record[0x2e] * ((quality >> 2) + 75 - (other >> 2)) / 100`

So turning quality up makes a sale more attractive AND dearer to supply; the `+0xAC` control does
the opposite on both. ⚠ `+0xAC` is named for its offset on purpose -- this port once called it a
customer count and it is not one.

## The ride screen -- `FUN_001D4FD0`, and it is slot 3 of the ride's vtable

Slot 3 is the lifecycle hook (see `laptop-screens.md`); on the ride screen it is where the sliders
are committed. The block that matters:

```c
if (state < 3) {                                  // the settings tab only
    ride = this[0x2FC];
    FUN_001DA740(this + 0xB98);                   // slider 1 update
    vt[0x30C](ride, *(u16*)(this + 0xBC6));       // -> SPEED
    if (extra) {
        FUN_001DA740(this + 0xCD0);               // slider 2
        FUN_001DA740(this + 0xE08);               // slider 3
        vt[0x314](ride, *(u16*)(this + 0xCFE));   // -> the THIRD setting
        vt[0x31C](ride, *(u16*)(this + 0xE36));   // -> DURATION
    }
}
```

⭐ **A slider's current value is at widget + 0x2E**, and three widgets agree on it: 0xB98/0xBC6,
0xCD0/0xCFE, 0xE08/0xE36. One offset read three times is the evidence; once would not have been.

⭐ **The setters, from `findings/ride-value-producer.md`:** `vt+0x30C` is `0x118378`, which writes
`P+0xE8` -- speed; `vt+0x31C` is `0x1183C8`, which writes `P+0xF0` -- duration. Their getters are
`vt+0x2F4` (`0x118398`) and `vt+0x304` (`0x1183E8`).

⭐ **So `vt+0x314` is a THIRD operating setting**, the middle slider, whose getter by the same
spacing is `vt+0x2FC`. The screen authors it as `capacityslider`. ⚠ Its target field is NOT read
here, and this port has no field for it: `NativeRideValue.State` carries Speed and Duration only.

⚠⚠ **SLIDERS 2 AND 3 ARE CONDITIONAL.** `extra` is `kindOf(ride) != 1 && this[0x18D4] >= 2`, and
`vt+0xA4` is the kind getter (the same one `FUN_0015C710` asks every object). Kind 1 is `Coaster`.
So on a coaster ONLY the speed slider is committed. What `this[0x18D4]` counts is unread.

## Excitement: live from two of the three sliders

`NativeRideValue.Calculate` is the value/excitement producer and it is already in this port:

```
speed    = clamp((Speed << 12) / 100, 0xC00, 0x1400)     // x0.75 .. x1.25
duration = Duration << 12;  if kind != Coaster: /= 5
duration = clamp(duration, 0xC00, 0x1400)
value    = min(100, (basis * ((speed * duration) >> 12)) >> 12)
```

`ParkRide.Value` already recomputes this from the live fields, so moving speed or duration moves
excitement with no further wiring. ⭐ **Capacity has no term in it** -- which is consistent with
`vt+0x314` being a separate setting the value producer never reads.

## ⚠⚠ Reliability is NOT recomputed from the sliders

`FUN_00198A98(a,b,c)` is the reliability arithmetic --
`((Half(a) + Half(b)) / 2) * c` with `Half(p) = 0x800 + p/2` -- and
`RideCatalogue.ShopfrontReliability` folds it with the tier's durations into `100 - min(v, 100)`.

**A caller census over the whole image finds exactly ONE caller, `0x198B08`, in the shopfront
path.** Nothing computes reliability from a placed ride's live settings. So a placed ride's
"State of Repair" is its `Condition`, which WEARS, and the sliders can only reach it *indirectly*
by changing how fast it wears -- the tier's `MinSpeedDamage`, `MinCapacityDamage` and `WearRate`
are the per-ride-cycle damage inputs the shopfront preview uses as constants.

⭐ That matters for the wiring: dragging a slider must NOT change a reliability number on the spot.
Anything that did would be inventing a behaviour the console does not have.

## What is still unread, and must be before it is wired

1. **What `vt+0x314` writes** -- the capacity field's offset and its consumer.
2. **The ride screen's clamps.** The shop's live in its setup `0x1D6D28`; the ride's equivalent is
   its own setup (vtable slot 19, `0x1D4C80`) and is not read.
3. **What `this[0x18D4]` counts**, which gates two of the three sliders.
4. **Where wear is applied per ride cycle** -- the consumer of `MinSpeedDamage` /
   `MinCapacityDamage` / `WearRate` on a live ride, as opposed to the shopfront preview.
5. ⚠ `ride-value-producer.md`'s own warning stands: the final speed bridge
   (`0x118240 -> 0x1FA818 -> 0x1C0DE8`) and whether `VAR_DURATION` is the same quantity as the
   native operating duration are NOT established. Do not overwrite script variables on the strength
   of this.
