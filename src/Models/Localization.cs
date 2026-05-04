namespace MycopunkDumper;

public class Localization
{
    public int ID;                  // TextBlockGroup.id
    public LocalizationBlock[] Blocks;

    public class LocalizationBlock
    {
        public string Text;         // primary text
        public string UniqueID;     // optional uniqueID stamp from CSV import
    }
}
