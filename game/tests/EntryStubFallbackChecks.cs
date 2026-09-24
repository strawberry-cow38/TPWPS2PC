using System;
using System.Linq;
using System.Text;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Port-policy checks, not evidence of console defaults or parser call paths.</summary>
public static class EntryStubFallbackChecks
{
    const string X = "UsageInfo.EntryCellStandPosX";
    const string Y = "UsageInfo.EntryCellStandPosY";

    public static void Run(string disc, Action<bool, string> check)
    {
        var stub = new ParkCell(13, 17);
        bool Missing(RideDefinition d) => !d.Fields.ContainsKey(X) && !d.Fields.ContainsKey(Y)
            && !d.Blocks.ContainsKey(X) && !d.Blocks.ContainsKey(Y);
        var worlds = new[]
        {
            (World: "JUNGLE", Paths: new[] { "/Shops/Coconut/Coconut.sam" }),
            (World: "FANTASY", Paths: new[] { "/Shops/sburger/sBurger.sam", "/Shops/fries/Fries.sam", "/Shops/icecream/icecream.sam" }),
            (World: "SPACE", Paths: new[] { "/Shops/burger/Burger.sam", "/Shops/fries/fries.sam" })
        };
        foreach (var world in worlds)
        {
            // One owned library per world; never change the audit's active archive.
            using var library = new AssetLibrary(disc);
            library.OpenWad("/DATA/" + world.World + ".WAD");
            RideDefinition Read(string path) => RideDefinition.Parse(
                Encoding.ASCII.GetString(library.Wad.Read(library.Wad.Find(path))), path);
            foreach (string path in world.Paths)
            {
                var definition = Read(path);
                string name = world.World + path;
                var fp = Park.Footprint.From(definition.Shape);
                check(Missing(definition) && definition.Sells && fp.Width == 2 && fp.Height == 2
                      && fp.EntryX >= 0 && fp.EntryX < 2 && fp.EntryY >= 0 && fp.EntryY < 2
                      && !StandingServicePose.TryCreate(definition, stub, 0, out _),
                      name + ": raw missing pair; eligible footprint; authored-only rejects");

                bool allTurns = true;
                // Fixed, nonempty range; evaluate every turn even after an earlier failure.
                foreach (int turn in Enumerable.Range(0, 4))
                {
                    var placement = new Placement();
                    placement.Arm(definition, name, 0, fp);
                    placement.Turn(turn);
                    bool created = StandingServicePose.TryCreateAtEntryStub(definition, turn, stub, out var pose);
                    allTurns &= created && pose.IsEntryStubFallback
                        && pose.CellPoint.DistanceTo(new Vector2(13.5f, 17.5f)) < .0001f
                        && pose.HeightCell == new ParkCell(13, 17)
                        && pose.Inward == new Vector2I(-placement.Turned.EntryDX, -placement.Turned.EntryDY);
                }
                check(allTurns, name + ": all four turns use actual stub centre, height and placement inward; fallback tagged");
                check(Missing(definition), name + ": fallback leaves both raw keys absent");
            }

            if (world.World != "JUNGLE") continue;
            var ice = Read("/Shops/IceCream/IceCream.sam");
            check(ice.Fields.ContainsKey(X) && ice.Fields.ContainsKey(Y)
                  && StandingServicePose.TryCreate(ice, stub, 0, out _)
                  && !StandingServicePose.TryCreateAtEntryStub(ice, 0, stub, out _),
                  "JIceCream: authored coordinates reject fallback");
            var gift = Read("/Shops/GiftShop/GiftShop.sam");
            var giftFp = Park.Footprint.From(gift.Shape);
            check(Missing(gift) && gift.Sells && giftFp.Width == 3 && giftFp.Height == 3
                  && !StandingServicePose.TryCreateAtEntryStub(gift, 0, stub, out _),
                  "GiftShop: missing-coordinate 3x3 shop rejects fallback");
            var monkey = Read("/Rides/Monkey/Monkey.sam");
            check(!monkey.Sells && !StandingServicePose.TryCreateAtEntryStub(monkey, 0, stub, out _),
                  "Monkey: ordinary ride rejects fallback");
            check(!StandingServicePose.TryCreateAtEntryStub(Read(world.Paths[0]), 0, null, out _),
                  "Coconut: null entrance rejects fallback");

            foreach (string axis in new[] { X, Y })
            foreach (string value in new[] { "0.5", "malformed", "NaN" })
            {
                // Fresh real parse for each adversarial raw-field fixture, never shared.
                var partial = Read(world.Paths[0]);
                bool missingBefore = Missing(partial);
                partial.Fields[axis] = value;
                check(missingBefore && !StandingServicePose.TryCreateAtEntryStub(partial, 0, stub, out _)
                      && partial.Fields[axis] == value && !partial.Fields.ContainsKey(axis == X ? Y : X),
                      $"Coconut: only {axis}={value} rejects fallback without filling other coordinate");
            }
            foreach (string axis in new[] { X, Y })
            {
                string source = Encoding.ASCII.GetString(library.Wad.Read(library.Wad.Find(world.Paths[0])));
                var block = RideDefinition.Parse(source + "\n" + axis + "\n---\nmalformed\n---\n", world.Paths[0]);
                check(block.Blocks.ContainsKey(axis) && !block.Fields.ContainsKey(axis)
                      && !StandingServicePose.TryCreateAtEntryStub(block, 0, stub, out _),
                      $"Coconut: supplied {axis} block is malformed rather than absent");
            }
            var noEntry = Read(world.Paths[0]);
            noEntry.Blocks["Info.Shape"] = new[] { "**", "**" };
            check(Missing(noEntry) && !StandingServicePose.TryCreateAtEntryStub(noEntry, 0, stub, out _),
                  "Coconut: 2x2 footprint without entry rejects fallback");
        }
    }
}
