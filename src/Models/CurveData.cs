namespace MycopunkDumper;

// Flattened UnityEngine.AnimationCurve.
public class CurveData
{
    public string PreWrapMode;
    public string PostWrapMode;
    public Key[] Keys;

    public class Key
    {
        public float Time;
        public float Value;
        public float InTangent;
        public float OutTangent;
        public float InWeight;
        public float OutWeight;
        public string WeightedMode;
    }
}
