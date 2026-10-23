using System.Collections.Generic;

namespace MycopunkDumper;

// Codex datalog. Text logs are TextBlocks entries (key = ID); image logs are Texture2D assets in the `datalogs` bundle.
public class DataLog
{
    public string Kind;          // "text" | "image"
    public string Folder;        // codex folder (ID prefix before '/'), null for the root list
    public string FolderLabel;   // TextBlocks.GetString(Folder, 0)
    public string CodexLabel;    // button label in the codex list
    public string Title;         // text: TextBlocks [0]; image: "Data Log // <date>" as ImageLogWindow shows it
    public string Body;          // text: TextBlocks [1]
    public string Date;          // image: LogDates.txt entry
    public DataLogImage Image;
    public List<DataLogSource> Sources;

    public class DataLogImage
    {
        public string Asset;     // Texture2D name in the `datalogs` bundle
        public int Width;
        public int Height;
        public string Format;
        public bool Readable;    // Texture2D.isReadable — false means GetPixels/EncodeToPNG need a GPU blit
    }
}

public class DataLogSource
{
    public string Kind;          // "scene" | "incursion" | "globalEvent" | "prefab"
    public string Scene;
    public int? Count;           // scene: TextLogInteractables with this ID
    public string Mission;       // incursion: missions key
    public int? MinFloor;
    public string GlobalEvent;   // globalEvents key
    public string Trigger;       // "start" | "end"
    public string Prefab;
}
