using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

public partial class SideshowPresentationSmoke
{
    async Task SpinnerBoundsFixture(AssetResourceDatabase.Entry entry)
    {
        // Literal expected values, NOT computed through SideshowSpinner or the Viewer handler.
        // Each press starts from a fresh UNPLACED fixture. Price has native unsigned16 storage;
        // 65535 tests the getter's 0xffff case, rather than pretending it reads as signed -1.
        (int Row, int Raw, int Down, int Up)[] cases =
        {
            (7,-1,1,1), (7,0,0,0), (7,1,1,2), (7,1000,999,1000), (7,1001,999,1000),
            (8,0,1,1), (8,1,1,2), (8,1000,999,1000), (8,1001,999,1000), (8,65535,999,1000),
        };
        int id=900100;
        foreach (var (row,raw,down,up) in cases)
        foreach (int by in new[]{-1,1})
        {
            var ride=Fixture(entry,id++,row==7?raw:30);
            if(row==8) ride.SideshowPrice=(ushort)raw;
            var before=Settings(ride);
            Bind(ride); await Frames();
            int expected=by<0?down:up;
            string label=$"bounds fixture: canonical{row} raw{raw} nudge{by}";
            if(row==7 && raw==0)
            {
                Check(!Hits("_rowArrows").ContainsKey(7),label+": zero prize arrows absent");
                // Separate explicit HANDLER injection: an absent control cannot emit GUI input.
                StaleHandlerFixture("OnRowNudge",row,by); await Frames();
                Check(Settings(ride)==before,label+": stale handler preserves zero prize and sibling fields");
            }
            else
            {
                await Click(Hits("_rowArrows")[row],by>0);
                Check(_nudges[^1]==(row,by),label+": actual GUI event keeps canonical identity");
                var actual=Settings(ride);
                Check(actual.Prize==(row==7?expected:before.Prize)
                    && actual.Price==(row==8?expected:before.Price) && actual.Chance==before.Chance,
                    label+": literal bounded result; only targeted money field changes");
                string digits=expected==1000?"1,000":expected.ToString();
                Check(_panel.RowsDrawn[row].Text==digits,label+": bounded digits reach actual draw call");
            }
            Check(ride.Winners==17 && ride.Customers==19,label+": play counters untouched");
        }
        GD.Print("SIDESHOW BOUNDS fixture: twenty independent raw/arrow cases; normal input except explicitly labelled zero-prize stale-handler injections; not passive focus/controller-frame proof");
    }
}