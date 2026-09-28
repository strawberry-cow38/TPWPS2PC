using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

/// <summary>The walking grid, rendering/path grid and path tool as one ownership unit.
/// The Viewer uses TWO HeightField wrappers over ONE mutable Cells array. Their Step values
/// need not be identical. This is ground only, not a complete world/Viewer snapshot.</summary>
public static class ParkGroundSnapshot
{
    public const int Version = 1;
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required ParkPaths.State Paths { get; init; }
        public required bool RenderUsesPathField { get; init; }
        public required float RenderStep { get; init; }
        public required PathTool.State Tool { get; init; }
    }
    public sealed record Owners(ParkPaths Paths, Model.HeightField RenderField, PathTool Tool);
    static void Require(bool ok,string why)
    {if(!ok)throw new ArgumentException("Invalid ground snapshot: "+why);}
    public static State Capture(ParkPaths paths, Model.HeightField renderField, PathTool tool,
        string terrainKey, string pathPiecesKey)
    {
        ArgumentNullException.ThrowIfNull(paths);ArgumentNullException.ThrowIfNull(renderField);
        Require(renderField.Width==paths.Field.Width && renderField.Height==paths.Field.Height
            && ReferenceEquals(renderField.Cells,paths.Field.Cells),"walking/render cells are not shared");
        Require(tool==null || ReferenceEquals(tool.SnapshotField,renderField),"tool does not own the render field");
        Require(float.IsFinite(renderField.Step)&&renderField.Step>=0&&renderField.Step<=65536,"render step");
        return new State
        {
            Version=Version,Paths=paths.CaptureState(terrainKey),
            RenderUsesPathField=ReferenceEquals(renderField,paths.Field),RenderStep=renderField.Step,
            Tool=tool?.CaptureState(terrainKey,pathPiecesKey)
        };
    }
    /// <summary>Fresh logical owners; no asset mutation, path repaint or event publication.
    /// Asset keys must be independently resolved by caller. Bind events only after ALL owners stage.</summary>
    public static Owners Restore(State state,string expectedTerrainKey,string expectedPiecesKey,
        Model terrain,PathPieces pieces) => RestoreCore(state,expectedTerrainKey,expectedPiecesKey,terrain,pieces,false);

    /// <summary>The production Viewer binds Park and PathTool to Model.Field itself, while
    /// walking has another wrapper sharing its bytes. Supply a FRESH STAGED terrain model,
    /// never a live/cached asset owner. Its field is changed only after every DTO validates.
    /// This explicit variant leaves the old live world untouched when used as intended.</summary>
    public static Owners RestoreIntoTerrainField(State state,string expectedTerrainKey,string expectedPiecesKey,
        Model stagedTerrain,PathPieces pieces) => RestoreCore(state,expectedTerrainKey,expectedPiecesKey,stagedTerrain,pieces,true);

    static Owners RestoreCore(State state,string expectedTerrainKey,string expectedPiecesKey,
        Model terrain,PathPieces pieces,bool bindModelField)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(terrain);
        Require(state.Version==Version && state.Paths!=null,"version/paths");
        Require(float.IsFinite(state.RenderStep)&&state.RenderStep>=0&&state.RenderStep<=65536,"render step");
        Require(!state.RenderUsesPathField || state.RenderStep==state.Paths.Step,"shared wrapper step conflict");
        Require(!bindModelField || !state.RenderUsesPathField,"terrain-field binding needs distinct walking/render wrappers");
        var paths=ParkPaths.FromState(state.Paths,expectedTerrainKey,terrain);
        var render=bindModelField?terrain.Field:state.RenderUsesPathField?paths.Field:new Model.HeightField
            {Width=paths.Field.Width,Height=paths.Field.Height,Cells=paths.Field.Cells,Step=state.RenderStep};
        var tool=state.Tool==null?null:PathTool.FromState(state.Tool,expectedTerrainKey,expectedPiecesKey,
            terrain,state.Tool.HasPieces?pieces:null,render);
        // No further validators/callbacks after committing the supplied staged field.
        if(bindModelField) { render.Cells=paths.Field.Cells;render.Step=state.RenderStep; }
        return new(paths,render,tool);
    }
}
