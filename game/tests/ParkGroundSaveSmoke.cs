using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using TPW.PS2.Data;
using DataModel=TPW.PS2.Data.Model;

namespace TPWPS2Viewer.Tests;

/// <summary>Real-disc ground+render placement FILE consumer. Not a full Viewer load.</summary>
public partial class ParkGroundSaveSmoke:Node
{
    sealed record Cut
    {
        public required ParkGroundSnapshot.State Ground {get;init;}
        public required Park.State Park {get;init;}
    }
    int _checks;
    void Check(bool ok,string name){_checks++;if(!ok)throw new Exception(name);}
    static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    static Node3D Asset(string key)
    {var n=new Node3D {Name="SavedModel"};n.SetMeta("save_asset",key);return n;}
    public override void _Ready()
    {
        try
        {
            using var disc=new Disc(System.Environment.GetEnvironmentVariable("TPW_PS2_DISC")??throw new Exception("TPW_PS2_DISC missing"));
            var pieces=PathPieces.Read(disc);
            foreach(string world in new[]{"JUNGLE","FANTASY","HALLOW","SPACE"})
            {
                var f=disc.Files().Single(f=>f.Path=="/DATA/"+world+".WAD");var wad=new WadArchive(disc.Read(f.Extent,f.Size));
                var terrain=new DataModel(wad.Read(wad.Find("/terrain/terrain_1.mps")));
                string key=world+"/terrain_1",pieceKey="retail/pathpieces";
                var paths=new ParkPaths(terrain);var render=terrain.Field;
                paths.Field.Cells=render.Cells; // EXACT production Viewer.WalkGrid join, not wrapper identity.
                var tool=new PathTool(terrain,pieces);
                Check(tool.Ready,world+": native path pieces ready");
                paths.Field.Step=.75f;render.Step=1.25f; // non-vacuous separate-wrapper control.
                var source=new Park {Field=render,GroundMaterial=new StandardMaterial3D(),Origin=new(0,0)};
                Park restored=null;
                try
                {
                    source.Build(render.Width,render.Height);AddChild(source.Root);
                    var fp=Park.Footprint.From(new[]{"*2E","*"});
                    var at=paths.Cells.First(c=>c.X>2&&c.Z>2&&c.X+8<render.Width&&c.Z+7<render.Height
                        &&source.CanPlace(fp,c.X,c.Z)&&Enumerable.Range(0,6).All(dx=>Enumerable.Range(0,6).All(dz=>tool.CanLay(c.X+dx,c.Z+dz)&&paths.CanBuild(new(c.X+dx,c.Z+dz)))));
                    int x=at.X,y=at.Z;
                    Check(source.TryPlace(Asset("fixture/ride"),fp,71,"fixture",x,y,0),world+": actual placement");
                    foreach(var p in source.Placed) for(int z=0;z<p.Fp.Height;z++)for(int xx=0;xx<p.Fp.Width;xx++)
                        if(p.Fp.Cells[xx,z])paths.Occupy(new[]{new ParkCell(p.X+xx,p.Y+z)});
                    source.Reserve(x+6,y+4,1,1);
                    byte originalTile=render.Cells[((y+3)*render.Width+x+1)*2+1];
                    tool.BeginLeg();Check(tool.Lay(x+1,y+3),world+": path laid");
                    tool.BeginLeg();Check(tool.Lay(x+2,y+3,PathTool.Kind.Queue,71,PathPieces.East),world+": owned queue laid");
                    Check(tool.Lay(x+3,y+3,PathTool.Kind.Queue,71,PathPieces.West),world+": second queue cell");
                    Park.StateBindings Bind(ParkPaths p,DataModel.HeightField h)=>new(){SharedPaths=p,SharedField=h,
                        IdentifyNode=(_,n)=>n.GetMeta("save_asset").AsString(),BuildNode=Asset};
                    var cut=new Cut{Ground=ParkGroundSnapshot.Capture(paths,render,tool,key,pieceKey),Park=source.CaptureState(Bind(paths,render))};
                    string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"tpw-ground-"+Guid.NewGuid().ToString("N")+".tpwsave");
                    Cut loaded;
                    try{ParkSaveFile.Write(file,cut);loaded=ParkSaveFile.Read<Cut>(file);}
                    finally{if(System.IO.File.Exists(file))System.IO.File.Delete(file);}
                    byte[] before=render.Cells.ToArray();
                    var stagedTerrain=new DataModel(wad.Read(wad.Find("/terrain/terrain_1.mps")));
                    var owners=ParkGroundSnapshot.RestoreIntoTerrainField(loaded.Ground,key,pieceKey,stagedTerrain,pieces);
                    restored=Park.FromState(loaded.Park,Bind(owners.Paths,owners.RenderField));
                    Check(before.SequenceEqual(render.Cells)&&!ReferenceEquals(owners.Paths.Field,owners.RenderField)
                        &&ReferenceEquals(owners.Paths.Field.Cells,owners.RenderField.Cells)
                        &&!ReferenceEquals(owners.RenderField.Cells,render.Cells)
                        &&ReferenceEquals(restored.Field,owners.RenderField)&&ReferenceEquals(owners.RenderField,stagedTerrain.Field),world+": exact two-wrapper/one-array ownership, old world untouched");
                    Check(owners.Paths.Field.Step==.75f&&owners.RenderField.Step==1.25f,world+": separate header state retained");
                    Check(Json(loaded.Park)==Json(restored.CaptureState(Bind(owners.Paths,owners.RenderField)))
                        &&Json(loaded.Ground)==Json(ParkGroundSnapshot.Capture(owners.Paths,owners.RenderField,owners.Tool,key,pieceKey)),world+": complete geometry state roundtrip");
                    restored.GroundMaterial=new StandardMaterial3D();restored.Rebuild();AddChild(restored.Root);
                    Check(restored.PlacedAt(x+1,y)?.Id==71&&restored.Reserved(x+6,y+4)
                        &&restored.OccupiedCells==source.OccupiedCells,world+": publish/rebuild retains real selection and occupancy");
                    void Both(Action<PathTool> action,string name)
                    {
                        action(tool);action(owners.Tool);
                        Check(render.Cells.SequenceEqual(owners.RenderField.Cells)
                            &&Json(tool.CaptureState(key,pieceKey))==Json(owners.Tool.CaptureState(key,pieceKey)),world+": "+name);
                    }
                    Both(t=>t.Lay(x+4,y+3),"continue active undo leg after load");
                    Both(t=>t.UndoLeg(),"undo restores same ground and queue state");
                    Both(t=>{t.BeginLeg();t.Lay(x+2,y+3,PathTool.Kind.Queue,71,PathPieces.East);},"queue re-laid");
                    Both(t=>Check(!t.TearUp(x+2,y+3),"owned queue directly refused"),"delete ownership rule");
                    Both(t=>t.ClearQueue(71),"ride-owned queue removal");
                    Both(t=>Check(t.TearUp(x+1,y+3),"path tear-up succeeds"),"path to original grass");
                    Check(owners.RenderField.Cells[((y+3)*render.Width+x+1)*2+1]==originalTile
                        &&owners.Paths.Kind(new(x+1,y+3))==paths.Kind(new(x+1,y+3)),world+": walking sees restored grass through byte alias");
                    source.Remove(71);restored.Remove(71);
                    Check(restored.PlacedAt(x,y)==null&&restored.OccupiedCells==0&&source.OccupiedCells==0,world+": real placement delete after load");
                    var bad=JsonNode.Parse(Json(loaded.Ground)).AsObject();bad["Paths"]["Occupied"][0].AsObject().Remove("X");
                    bool rejected=false;try{JsonSerializer.Deserialize<ParkGroundSnapshot.State>(bad.ToJsonString());}catch(JsonException){rejected=true;}
                    Check(rejected,world+": nested cell coordinate required, not defaulted to zero");
                    var badTool=loaded.Ground with { Tool=loaded.Ground.Tool with { Owners=Array.Empty<int>() } };
                    var untouchedModel=new DataModel(wad.Read(wad.Find("/terrain/terrain_1.mps")));
                    var untouchedBytes=untouchedModel.Field.Cells.ToArray();float untouchedStep=untouchedModel.Field.Step;
                    rejected=false;try{ParkGroundSnapshot.RestoreIntoTerrainField(badTool,key,pieceKey,untouchedModel,pieces);}
                    catch(ArgumentException){rejected=true;}
                    Check(rejected&&untouchedBytes.SequenceEqual(untouchedModel.Field.Cells)&&untouchedModel.Field.Step==untouchedStep,
                        world+": bad nested tool cannot partially change the supplied staged model field");
                    GD.Print($"GROUND SAVE PASS {world}: shared grid, selection, active undo, owner deletion, grass restoration");
                }
                finally{restored?.Root.Free();source.Root.Free();}
            }
            GD.Print($"PASS ParkGroundSaveSmoke {_checks} checks; geometry/paths/placement only, not whole Viewer load");GetTree().Quit();
        }
        catch(Exception e){GD.PrintErr("GROUND SAVE FAIL "+e);GetTree().Quit(2);}
    }
}
