namespace MycopunkDumper;

// Flattened game DamageData struct.
public class DamageInfo
{
    public float Damage;
    public string Effect;        // EffectType enum stringified
    public float EffectAmount;
    public string DamageFlags;   // DamageFlags flag-enum stringified
}
