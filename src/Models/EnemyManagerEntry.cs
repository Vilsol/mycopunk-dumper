using System.Collections.Generic;

namespace MycopunkDumper;

// Scene EnemyManager spawn configs: the most common one in full, plus each scene's differing fields (null = unset there).
public class EnemyManagerEntry
{
    public SortedDictionary<string, object> Default;
    public SortedDictionary<string, SortedDictionary<string, object>> Scenes;   // every scene with an EnemyManager; {} = Default
}
