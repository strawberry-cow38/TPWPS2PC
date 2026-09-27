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

⭐⭐ **`vt+0x314` WRITES `P+0xEC`, AND THE THREE SETTINGS ARE THREE ADJACENT WORDS.** Reading the
accessor block straight off the instructions settles it -- they sit in order and each is a one-line
leaf:

```
0x118384  sw -> P+0xE8    speed      (setter vt+0x30C, getter vt+0x2F4 at 0x11839C)
0x1183AC  sw -> P+0xEC    CAPACITY   (setter vt+0x314, getter vt+0x2FC at 0x1183C4)
0x1183D4  sw -> P+0xF0    duration   (setter vt+0x31C, getter vt+0x304 at 0x1183EC)
```

Three adjacent fields, three adjacent setter slots, three sliders. The screen's own setup reads
them back through `vt+0x2F4`, `vt+0x2FC` and `vt+0x304` in that order, which is the second reading
that agrees. ⚠ This port has no field for capacity: `NativeRideValue.State` carries Speed and
Duration only, because the value producer genuinely does not read capacity.

## The slider ranges, from the screen's setup `FUN_001D4C80`

Each slider is a widget with a **min / max / value triple**, and the value is stored `<< 16`:

| slider | widget | min | max | value |
|---|---|---|---|---|
| speed | `+0xB98` | `+0xBBC` | `+0xBC0` | `+0xBC4` |
| capacity | `+0xCD0` | `+0xCF4` = **1** | `+0xCF8` = `this[0x18D4]` | `+0xCFC` |
| duration | `+0xE08` | `+0xE2C` | `+0xE30` | `+0xE34` |

Each is clamped into its own range as it is read back out of the ride.

⭐⭐ **THE TWO READINGS CORROBORATE EACH OTHER.** The setup stores the value `<< 0x10`, and the
write-back reads a `u16` at `+0xBC6` / `+0xCFE` / `+0xE36` -- exactly two bytes on, the HIGH
halfword of that fixed-point word, which is its integer part. Neither read was made looking for the
other.

⭐ **And `this[0x18D4]` is the CAPACITY MAXIMUM**, which also explains the gate on sliders 2 and 3:
`>= 2` means "this ride holds more than one", so a single-car ride is offered neither.

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

## ⚠⚠ RELIABILITY IS RECOMPUTED LIVE -- I REPORTED THE OPPOSITE AND WAS WRONG

This section previously said reliability was not slider-driven. It is. Master, 2026-09-27:
*"it absolutely does recomp reliability live. your ctrl f just failed. dont assume, actually
research."* Both halves of that are correct.

**What I did wrong.** `FUN_00198A98` is the shopfront preview's arithmetic, and a caller census
over it finds exactly one caller. That census was TRUE. The conclusion drawn from it -- "nothing
computes reliability from live settings" -- does not follow from it: it says nothing about any
OTHER function computing reliability, and one does. A search bounded to one function cannot
answer a question about the whole program. This is the repo's own rule about negative searches,
broken by the person who wrote it down.

**The way in was the screen's own draw, not a search.** `FUN_001D5210` fills its four bars from:

| bar | source |
|---|---|
| Excitement | `vt+0x1D4`, the value producer |
| **Reliability** | **`vt+0x2EC`** |
| Repair | `FUN_00118228` = `ride[0xE4] >> 12`, the worn condition |
| Life | `FUN_00118238` = `ride[0x94]` |

`vt+0x2EC` is `FUN_001183F0`:

```c
wear     = vt[0x36C](ride, 1);
duration = vt[0x304](ride);              // the DURATION slider
v        = wear * duration * 9 >> 15;
return 100 - min(v, 100);
```

and `vt+0x36C` is per family -- `0x1B80E0` ride, `0x1E9E10` tour, `0x201F78` track. ⚠ Those are
exactly the three functions an earlier census in this document had already listed as reading BOTH
speed and capacity. The evidence was in my own output and I did not follow it.

### The wear term, `FUN_001B80E0`

```c
sp = (0x1000 - MinSpeedDamage) * ((speed << 12) / 100) >> 12;
sp = speed < 100 ? sp + MinSpeedDamage                  // added below 100
                 : (MinSpeedDamage + sp + 0x1000) / 2;  // averaged at or above
cp = (capacity << 12) / maxCapacity;                    // vt[0x2FC], the CAPACITY slider
t  = (0x1000 - MinCapacityDamage) * cp;
capTerm = (t >> 12) + MinCapacityDamage;
if (cp > 0xCCB)                                          // past four fifths of maximum
    capTerm = MinCapacityDamage + (t >> 12) + ((cp - 0xCCC) >> 6) * ((cp - 0xCCC) >> 6);
wear = ((sp + capTerm) / 2) * WearRate;
```

⭐ **So ALL THREE SLIDERS drive reliability**: speed and capacity through the wear term, duration
as its multiplier. And capacity's consumer -- listed as unread a page ago -- is this.

⭐⭐ **AND IT HAS A CONTROL THAT PASSES.** `ShopfrontReliability` is this same arithmetic frozen
at the middle: its `Half(p) = ((0x1000-p) * 0x800 >> 12) + p` is the speed term with
`(speed << 12)/100` equal to `0x800`, i.e. speed 50, and the capacity term at half of maximum.
Evaluating the live wear term at speed 50 and half capacity must reproduce the shopfront's `inner`
exactly, and it does -- `NativeRideReliability.MatchesShopfront` asserts it and the running port
prints `control PASS`.

Measured in the port, Acorn: speed 50 -> reliability 79, speed 100 -> reliability 72.
⚠ The shopfront shows 86 for the same ride because it assumes half capacity where the placed one
is at maximum; the two disagreeing by that much is the formula working, not a fault.

## What is still unread, and must be before it is wired

1. **Where the WORN condition comes from.** `ride[0xE4]` is the Repair bar and it is a separate
   quantity from the reliability computed above; what decrements it per ride cycle is not read.
2. **`vt+0x344`, the capacity MAXIMUM.** The wear term divides by it and the screen takes its
   slider bound from it; this port substitutes the tier's `CapacityParameter`, which is a stated
   assumption rather than a reading.
3. **Life**, `ride[0x94]`, is read but nothing is known about what moves it.
4. ⚠ Superseded -- the consumer of `MinSpeedDamage` /
   `MinCapacityDamage` / `WearRate` on a live ride, as opposed to the shopfront preview.
3. ⚠ `ride-value-producer.md`'s own warning stands: the final speed bridge
   (`0x118240 -> 0x1FA818 -> 0x1C0DE8`) and whether `VAR_DURATION` is the same quantity as the
   native operating duration are NOT established. Do not overwrite script variables on the strength
   of this.
