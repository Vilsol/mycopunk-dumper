using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json;

namespace MycopunkDumper;

partial class Plugin
{
    private static readonly System.Text.RegularExpressions.Regex RichTextTag =
        new(@"<[^>]+>", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly HashSet<string> PlainStripTargets = new(StringComparer.Ordinal)
    {
        "Name", "Description", "Title", "TypeName", "MissionTypeName", "TitleAndDescription"
    };

    // Walks the serialized JSON tree and injects a Plain<FieldName> sibling for any
    // PlainStripTargets-listed string field that contains Unity rich-text tags. Saves
    // consumers from running an HTML stripper on every entity.
    private static void InjectPlainTextSiblings(Newtonsoft.Json.Linq.JToken token)
    {
        switch (token)
        {
            case Newtonsoft.Json.Linq.JObject obj:
            {
                // Capture additions while iterating; mutate after to avoid InvalidOperationException.
                var pending = new List<(Newtonsoft.Json.Linq.JProperty after, string key, string value)>();
                foreach (var prop in obj.Properties())
                {
                    if (PlainStripTargets.Contains(prop.Name) && prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        var s = (string)prop.Value;
                        if (!string.IsNullOrEmpty(s) && s.IndexOf('<') >= 0 && RichTextTag.IsMatch(s))
                        {
                            var plain = RichTextTag.Replace(s, string.Empty);
                            if (!ReferenceEquals(plain, s) && plain != s)
                                pending.Add((prop, "Plain" + prop.Name, plain));
                        }
                    }
                    InjectPlainTextSiblings(prop.Value);
                }
                foreach (var (after, key, value) in pending)
                    after.AddAfterSelf(new Newtonsoft.Json.Linq.JProperty(key, value));
                break;
            }
            case Newtonsoft.Json.Linq.JArray arr:
                foreach (var item in arr) InjectPlainTextSiblings(item);
                break;
        }
    }

    private static string UpgradeKey(UpgradeID id) => string.IsNullOrEmpty(id.Mod) ? id.ID.ToString() : $"{id.Mod}:{id.ID}";

    private static readonly System.Text.RegularExpressions.Regex CamelSplit =
        new(@"(?<=[a-z0-9])(?=[A-Z])", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly string[] PropertyTypePrefixes =
        { "UpgradeProperty_", "DirectiveProperty_", "SkinUpgradeProperty_" };

    // "UpgradeProperty_Bruiser_Cannonball" → "Bruiser Cannonball".
    private static string PrettifyPropertyType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return typeName;
        var s = typeName;
        foreach (var prefix in PropertyTypePrefixes)
            if (s.StartsWith(prefix)) { s = s.Substring(prefix.Length); break; }
        s = s.Replace('_', ' ');
        s = CamelSplit.Replace(s, " ");
        return s;
    }

    private static string StripRichText(string s) => string.IsNullOrEmpty(s) ? s : RichTextTag.Replace(s, "").Trim();


    private static Upgrade.DIcon BuildIcon(UnityEngine.Sprite icon)
    {
        if (icon == null || icon.texture == null) return null;
        return new Upgrade.DIcon
        {
            Rect = [icon.rect.m_XMin, icon.rect.m_YMin, icon.rect.m_Width, icon.rect.m_Height],
            Texture = icon.texture.name
        };
    }

    /// <summary>
    /// Build the cosmetic-specific shape for a <c>SkinUpgrade</c>. Pulls together the
    /// SkinUpgrade-only fields plus a per-property summary that resolves the most
    /// useful subclass data. Previews are populated in a post-pass once
    /// ApplicableTo is known.
    /// </summary>

    private static T? GetPrivateFieldNullable<T>(object obj, string name) where T : struct
    {
        var t = obj.GetType();
        while (t != null)
        {
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (f != null && f.FieldType == typeof(T)) return (T)f.GetValue(obj);
            t = t.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Build the relative-path map for skin previews. Output paths follow the
    /// SkinRenderer's slug format (<c>{gear}_{skin}__{preset}/{...}.mp4</c>) so the
    /// dump is shippable independently of whether the renderer has actually run.
    /// Path is relative to the base extracted-dump directory (e.g.
    /// <c>~/MycopunkExtracted/&lt;version&gt;/</c>).
    /// </summary>

    private static string LocText(string key, int index = 0)
    {
        if (string.IsNullOrEmpty(key)) return null;
        try
        {
            return TextBlocks.TryGetString(key, index, out var text) ? text : null;
        }
        catch { return null; }
    }

    private static T GetPrivateField<T>(object obj, string name)
    {
        var t = obj.GetType();
        while (t != null)
        {
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (f != null) return (T)f.GetValue(obj);
            t = t.BaseType;
        }
        return default;
    }


    private static string NestedClipName(object animKey)
    {
        if (animKey == null) return null;
        var clip = GetPrivateField<UnityEngine.Object>(animKey, "clip") ?? GetPrivateField<UnityEngine.Object>(animKey, "Clip");
        return clip?.name;
    }


    private static string ColorAsString(object obj, string propName)
    {
        var t = obj.GetType();
        var p = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (p != null && p.PropertyType == typeof(UnityEngine.Color))
            return ((UnityEngine.Color)p.GetValue(obj)).ToString();
        return null;
    }
}
