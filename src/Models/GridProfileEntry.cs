namespace MycopunkDumper;

/// <summary>
/// A <c>GridProfile</c> ScriptableObject — the per-player-level upgrade-grid size curve. The grid
/// grows in steps: at each listed <c>Level</c> the grid becomes <c>Width</c>×<c>Height</c> cells.
/// </summary>
public class GridProfileEntry
{
    public GridSizeEntry[] GridSizes;

    public class GridSizeEntry
    {
        public int Level;
        public int Width;
        public int Height;
    }
}
