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
    private static MissionEntry BuildMission(Mission m)
    {
        var t = m.GetType();
        var e = new MissionEntry
        {
            ID = m.ID,
            Name = m.Name,
            MissionName = m.MissionName,
            TypeName = m.MissionTypeName,
            MissionTypeName = GetPrivateField<string>(m, "_missionTypeName"),
            Description = m.Description,
            Subclass = t.Name,
            MissionType = m.MissionType.ToString(),
            MissionFlags = m.MissionFlags.ToString(),
            CompatibleLevels = m.CompatibleLevels.ToString(),
            ShowHeader = m.ShowHeader,
            DisableWithoutCommand = m.DisableWithoutCommand,
            Selectable = m.Selectable,
            ShowInReplayMenu = m.ShowInReplayMenu,
            AutoStart = m.AutoStart,
            StartFirstObjective = m.StartFirstObjective,
            ExtractAtEnd = m.ExtractAtEnd,
            ResetWeekly = m.ResetWeekly,
            DisableDefaultRewards = m.DisableDefaultRewards,
            ShowButDontGiveAdditionalRewards = m.ShowButDontGiveAdditionalRewards,
            PlayStartVoicelineOnLateJoin = m.PlayStartVoicelineOnLateJoin,
            Index = m.Index,
            MinIntensity = m.MinIntensity,
            MinLevelToStart = m.MinLevelToStart,
            OverrideExtractDuration = m.OverrideExtractDuration,
            TeamReviveMultiplier = m.TeamReviveMultiplier,
            ExpectedDurationMultiplier = m.ExpectedDurationMultiplier,
            MissionXPMultiplier = m.MissionXPMultiplier,
            MissionScriptMultiplier = m.MissionScriptMultiplier,
            PlayStartVoicelineDuringDropDelay = m.PlayStartVoicelineDuringDropDelay,
            Color = m.MissionColor.ToString(),
            Icon = BuildIcon(m.MissionIcon),
            RawData = m
        };

        // StartVoiceline — Voiceline struct with id/priority
        try
        {
            var vl = m.MissionStartVoiceline;
            var vlid = GetPrivateField<string>(vl, "id");
            e.StartVoiceline = new MissionEntry.Voiceline
            {
                ID = vlid,
                Text = LocText(vlid, 0),
                Priority = GetPrivateField<int>(vl, "priority")
            };
        }
        catch { }

        // validScenes : SceneData[]
        var scenes = GetPrivateField<System.Array>(m, "validScenes");
        if (scenes != null)
        {
            var list = new List<MissionEntry.SceneRef>();
            foreach (var s in scenes)
            {
                if (s == null) continue;
                list.Add(new MissionEntry.SceneRef
                {
                    Scene = GetPrivateField<string>(s, "scene"),
                    LocationName = GetPrivateField<string>(s, "locationName")
                });
            }
            e.ValidScenes = list.ToArray();
        }

        // AdditionalRewards / RepeatRewards
        var ar = m.AdditionalRewards.Properties;
        if (ar != null) e.AdditionalRewards = ar.Where(x => x != null).Select(BuildLevelUnlock).ToArray();
        var rr = GetPrivateField<LevelUnlockList>(m, "RepeatRewards").Properties;
        if (rr != null) e.RepeatRewards = rr.Where(x => x != null).Select(BuildLevelUnlock).ToArray();

        return e;
    }

    private static ObjectiveEntry BuildObjective(ObjectiveBase o, string name)
    {
        var entry = new ObjectiveEntry
        {
            Name = name,
            Subclass = o.GetType().Name,
            Title = GetPrivateField<string>(o, "title"),
            AddWaypoint = GetPrivateField<bool>(o, "addWaypoint"),
            SetupUIOnActivate = GetPrivateField<bool>(o, "setupUIOnActivate"),
            ShowCompleteMessage = o.ShowCompleteMessage,
            IsSideObjective = o is ISideObjective,
            RawData = o
        };
        try
        {
            var sv = GetPrivateField<Voiceline>(o, "objectiveStartVoiceline");
            entry.StartVoiceline = new ObjectiveEntry.VoicelineRef { ID = sv.id, Text = LocText(sv.id, 0), Priority = sv.priority };
        }
        catch { }
        try
        {
            var cv = GetPrivateField<Voiceline>(o, "objectiveCompleteVoiceline");
            entry.CompleteVoiceline = new ObjectiveEntry.VoicelineRef { ID = cv.id, Text = LocText(cv.id, 0), Priority = cv.priority };
        }
        catch { }
        try
        {
            var infos = o.ObjectiveInfoList;
            if (infos != null)
            {
                entry.ObjectiveInfoList = infos
                    .Select(i => new ObjectiveEntry.ObjectiveInfoEntry
                    {
                        TitleID = i.title,
                        Title = LocText(i.title, 0),
                        DescriptionID = i.description,
                        Description = LocText(i.description, 0),
                        ShowNumberProgress = i.showNumberProgress
                    })
                    .ToArray();
            }
        }
        catch { }
        return entry;
    }


    private static MissionModifierEntry BuildMissionModifier(MissionModifier m)
    {
        var entry = new MissionModifierEntry
        {
            ID = m.ID,
            ModifierName = GetPrivateField<string>(m, "modifierName"),
            APIName = m.APIName,
            Name = m.Name,
            Description = m.Description,
            TitleAndDescription = m.TitleAndDescription,
            Subclass = m.GetType().Name,
            Flags = m.Flags.ToString(),
            Danger = m.Danger.ToString(),
            CanStack = m.CanStack,
            XPMultiplier = m.XPMultiplier,
            Color = ColorAsString(m, "Color"),
            TextColor = m.TextColor.ToString(),
            Icon = BuildIcon(m.Icon)
        };
        var im = GetPrivateField<Mission[]>(m, "incompatibleMissions");
        if (im != null) entry.IncompatibleMissions = im.Where(x => x != null).Select(x => x.ID).ToArray();
        var imd = GetPrivateField<MissionModifier[]>(m, "incompatibleModifiers");
        if (imd != null) entry.IncompatibleModifiers = imd.Where(x => x != null).Select(x => x.ID).ToArray();
        return entry;
    }


    private static Region BuildRegion(WorldRegion w)
    {
        var r = new Region
        {
            ID = w.ID,
            NameID = GetPrivateField<string>(w, "regionName"),
            RegionName = w.RegionName,
            ColoredRegionName = w.ColoredRegionName,
            LocalizedDescription = w.LocalizedDescription,
            Flags = w.Flags.ToString(),
            Color = w.Color.ToString(),
            LockRegion = w.LockRegion,
            Icon = BuildIcon(w.Icon)
        };

        var scenes = GetPrivateField<System.Array>(w, "sceneNames");
        if (scenes != null)
        {
            var list = new List<MissionEntry.SceneRef>();
            foreach (var s in scenes)
            {
                if (s == null) continue;
                list.Add(new MissionEntry.SceneRef
                {
                    Scene = GetPrivateField<string>(s, "scene"),
                    LocationName = GetPrivateField<string>(s, "locationName")
                });
            }
            r.Scenes = list.ToArray();
        }

        return r;
    }
}
