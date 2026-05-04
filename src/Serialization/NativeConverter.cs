using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace MycopunkDumper;

public class NativeConverter : JsonConverter
{
    public static ManualLogSource Logger;

    /// <summary>
    /// Global index from Unity runtime <c>Object.GetInstanceID()</c> to a semantic reference key
    /// (e.g. <c>"upgrade:99770"</c>, <c>"resource:saxonite"</c>). Populated by
    /// <c>Plugin.Start</c> before serialization. <see cref="WriteJson"/> rewrites every
    /// <c>{"instanceID":N}</c> reference whose <c>N</c> appears in this index, adding an
    /// <c>"@ref"</c> sibling field so consumers can resolve the reference without a separate lookup table.
    /// </summary>
    public static readonly Dictionary<int, string> InstanceRefs = new();

    // Unity's JsonUtility emits bare Infinity / -Infinity / NaN tokens which are not RFC 8259 JSON.
    // Quote them so the overall dump is valid JSON.
    private static readonly Regex NonStandardFloat = new(@"(?<=[:\[,])\s*(-?Infinity|NaN)\s*(?=[,\]\}])", RegexOptions.Compiled);
    private static readonly Regex InstanceIDRef = new(@"\{""instanceID"":(-?\d+)\}", RegexOptions.Compiled);

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        try
        {
            var raw = JsonUtility.ToJson(value);
            raw = NonStandardFloat.Replace(raw, m => "\"" + m.Value.Trim() + "\"");
            raw = InstanceIDRef.Replace(raw, m =>
            {
                if (int.TryParse(m.Groups[1].Value, out var id) && id != 0 && InstanceRefs.TryGetValue(id, out var refKey))
                    return $"{{\"instanceID\":{id},\"@ref\":\"{refKey}\"}}";
                return m.Value;
            });
            writer.WriteRawValue(raw);
        }
        catch (Exception e)
        {
            Logger?.LogError($"NativeConverter failed for {value?.GetType().Name}: {e.Message}");
            writer.WriteNull();
        }
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(HexMap)
            || typeof(UpgradeProperty).IsAssignableFrom(objectType)
            || typeof(DirectiveProperty).IsAssignableFrom(objectType)
            || typeof(IUpgradable).IsAssignableFrom(objectType)
            || typeof(Mission).IsAssignableFrom(objectType)
            || typeof(ObjectiveBase).IsAssignableFrom(objectType);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}
