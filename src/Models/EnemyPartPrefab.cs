using System.Collections.Generic;

namespace MycopunkDumper;

// Enemy part prefab (core, leg, arm, attachment) referenced by EnemyClass weighted lists.
// Values are pre-scaling: difficulty.EnemyHealthScaling / EnemyDamageScaling apply on top.
public class EnemyPartPrefab
{
    public string MountedOn;                     // IArmTip prefabs are spawned on this leg prefab (Global.GunArmLarge)
    public EnemyPartComponent[] Components;
}

// One gameplay MonoBehaviour (EnemyPart subclass, EnemyIKLeg, Weakpoint) inside a part prefab.
public class EnemyPartComponent
{
    public string Path;                          // child transform path relative to prefab root ("" = root)
    public string Subclass;
    public SortedDictionary<string, object> Fields;  // serialized numeric/enum/DamageData fields + consts, by field name
}
