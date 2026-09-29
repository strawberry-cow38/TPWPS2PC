using System;
using System.Linq;
using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

public partial class PlayerPathSession
{
    // CursorCell/CellAtScreen were read before invoking here: plane intersection and
    // nearest-cell search only. This samples the actual picker; it does not set the
    // cursor, invoke a tool handler, advance simulation, or replace an input event.
    void ObserveToolInput(string phase)
    {
        if (viewer == null || !GodotObject.IsInstanceValid(viewer)) return;
        object[] args = { -1, -1 };
        bool picked = (bool)viewer.GetType().GetMethod("CursorCell", Hidden).Invoke(viewer,args);
        int x = (int)args[0], y = (int)args[1];
        var mouse = GetViewport().GetMousePosition();
        var camera = GetViewport().GetCamera3D();
        var track = Read<object>(viewer,"_trackTool");
        object Field(string name) => track?.GetType().GetField(name,
            BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(track);
        var layout = (TrackLayout)Field("Layout");
        var park = Read<Park>(viewer,"_park");
        var paths = Read<PathTool>(viewer,"_paths");
        object candidate = null;
        if (picked && layout != null)
        {
            var (end,n,sx,sz) = layout.Leg(new ParkCell(x,y));
            var previous = layout.Waypoints[^1];
            candidate = new { end = new[]{end.X,end.Z}, steps=n, sx,sz,
                previous = new[]{previous.X,previous.Z}, previousPiece=layout.PieceAt(previous)?.Type,
                blocks = Enumerable.Range(0,n+1).Select(i => {
                    var b=previous.Offset(sx*i,sz*i);
                    return new { x=b.X,z=b.Z,isEnd=i==n,
                        cells=Enumerable.Range(0,4).Select(k=> {
                            int cx=b.X+(k&1),cz=b.Z+(k>>1);
                            bool inside=park!=null&&cx>=0&&cz>=0&&cx<park.Width&&cz<park.Height;
                            return new {x=cx,z=cz,inside,
                                playable=inside?(bool?)park.IsPlayable(cx,cz):null,
                                vacant=inside?(bool?)park.Vacant(cx,cz):null,
                                pathKind=inside?paths?.KindAt(cx,cz).ToString():null,
                                ownPiece=layout.PieceAt(new ParkCell(cx,cz))?.Type};
                        }).ToArray()};
                }).ToArray()};
        }
        var status=Read<Label>(viewer,"_toolStatus");
        var panel=Read<Control>(viewer,"_panel");
        Log("tool-input",new {phase,ticks=Read<int>(viewer,"_parkTicks"),
            mouse=new[]{mouse.X,mouse.Y},picked,x,y,
            panelVisible=panel?.Visible,status=status?.Text,
            cameraPosition=camera==null?null:new[]{camera.GlobalPosition.X,camera.GlobalPosition.Y,camera.GlobalPosition.Z},
            trackPresent=track!=null,price=Field("Price"),trackLegOk=Read<bool>(viewer,"_trackLegOk"),
            previewCost=Read<int?>(viewer,"_previewCost"),previewStock=Read<int?>(viewer,"_previewStock"),
            waypoints=layout?.Waypoints.Select(p=>new[]{p.X,p.Z}).ToArray(),
            pieceCount=layout?.Pieces.Count,closed=layout?.Closed,candidate});
    }
}
