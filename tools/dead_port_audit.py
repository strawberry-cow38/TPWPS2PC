#!/usr/bin/env python3
"""Find sim behaviour the running game never calls.

    python3 tools/dead_port_audit.py [--all]

⭐⭐ THIS PORT'S DOMINANT BUG IS CODE THAT IS CORRECT, AUDITED AND NEVER REACHED, and it has now
bitten in both TPW ports independently. The PSX one lost its needs clock, its idle pass and
EjectEveryone in a single day. The PS2 one shipped `VisitorNeeds.WantsToGoHome` decoded off the
real function, documented down to the half of the gate a casual reading misses, with its own
arithmetic checks green -- and called from nowhere, so no visitor could ever go home and every
park filled up monotonically forever.

⚠ None of that fails a build, an audit or a run. It is invisible by construction: the audits pass
BECAUSE the logic is right, and the feature is absent because nothing invokes it. The two facts
never touch.

So this looks for the shape rather than waiting for the next one: a public entry point that the
AUDITS exercise, the sim does not call internally, and the host never names.

⚠ CANDIDATES, NOT VERDICTS. It matches on identifier, so it cannot see a call made through an
interface, a delegate or a vtable-style indirection, and it lists value-type helpers that only an
audit has reason to name. Confirm each before acting -- but a name here has earned a look.

Adapted from the same tool in the PSX port (`~/tpw-psxpc/tools/dead_port_audit.py`); this port
keeps its sim in core/TPW.PS2.Data and its checks in tools/, not tests/.
"""
import glob, os, re, sys, collections

SKIP = {"get", "set", "if", "for", "while", "switch", "return", "new", "throw", "lock", "using", "catch"}
DECL = re.compile(r"public\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\],\s\?]+?\s(\w+)\s*\(")

SIM = "core/TPW.PS2.Data/*.cs"
HOST = ["game/**/*.cs", "launcher/**/*.cs", "core/TPW.PS2.Launcher/**/*.cs"]
CHECKS = ["tools/**/*.cs"]


def read(pats):
    out = []
    for p in pats:
        for f in glob.glob(p, recursive=True):
            out.append(open(f, encoding="utf-8", errors="ignore").read())
    return "\n".join(out)


def main():
    show_all = "--all" in sys.argv
    sim_files = sorted(glob.glob(SIM))
    game, checks, sim = read(HOST), read(CHECKS), read([SIM])

    found = collections.defaultdict(list)
    for f in sim_files:
        cls = os.path.basename(f)[:-3]
        txt = open(f, encoding="utf-8", errors="ignore").read()
        for m in DECL.finditer(txt):
            name = m.group(1)
            if name in SKIP or name == cls or name.startswith("_"):
                continue
            pat = r"\b" + re.escape(name) + r"\s*\("
            if len(re.findall(pat, game)) == 0 \
               and len(re.findall(pat, sim)) <= 1 \
               and len(re.findall(pat, checks)) > 0:
                if name not in [n for n, _ in found[cls]]:
                    found[cls].append((name, len(re.findall(pat, checks))))

    total = sum(len(v) for v in found.values())
    print(f"sim entry points the game never names: {total} candidates in {len(found)} files\n")
    for cls in sorted(found, key=lambda c: (-len(found[c]), c)):
        names = sorted(found[cls])
        print(f"  {cls}")
        for name, t in (names if show_all else names[:8]):
            print(f"      {name:<34} {t:>3} check refs")
        if not show_all and len(names) > 8:
            print(f"      ... and {len(names) - 8} more (--all)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
