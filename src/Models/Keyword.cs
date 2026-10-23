namespace MycopunkDumper;

// Tooltip keyword from Keywords.keywords (built from TextBlocks groups whose block[0] has `keyword` metadata).
public class Keyword
{
    public string Key;              // keys[0] — the term shown in tooltips
    public string[] Synonyms;       // keys[1..] — alternate spellings that resolve to the same keyword
    public string Color;            // colorHex, empty when none/invalid
    public string Description;      // TextBlocks.GetString(descriptionID, 1)
}
