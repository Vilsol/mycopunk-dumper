using System.Collections.Generic;

namespace MycopunkDumper;

// One build scene with a LevelData component, keyed by scene name.
public class Level
{
    public int BuildIndex;
    public string Path;
    public string LocationName;                       // already localized by LevelData.Awake
    public string Region;                             // regions key
    public string LevelFlags;
    public float MaxRailgunAngle;
    public SortedDictionary<string, int> Collectables; // PunchCollectable count per collectables key
    public string[] DataLogs;                         // dataLogs keys placed in this scene
}
