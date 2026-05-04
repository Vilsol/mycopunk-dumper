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
    private static Directive BuildDirective(DirectiveData dd)
    {
        var d = new Directive
        {
            ID = dd.ID,
            CanBeChosen = dd.CanBeChosen,
            Icon = BuildIcon(dd.Icon)
        };

        // TierWeights — TierModifier<float> is a struct with tier1..tier4 fields
        d.TierWeights = new Directive.TierWeightsEntry
        {
            Tier1 = GetPrivateField<float>(dd.TierWeights, "tier1"),
            Tier2 = GetPrivateField<float>(dd.TierWeights, "tier2"),
            Tier3 = GetPrivateField<float>(dd.TierWeights, "tier3"),
            Tier4 = GetPrivateField<float>(dd.TierWeights, "tier4")
        };

        if (dd.Directives.properties != null)
        {
            d.Properties = dd.Directives.properties.Where(p => p != null).Select(p =>
            {
                // NameID is `protected abstract string` — only reachable via reflection on the property's runtime type.
                var nameId = p.GetType().GetProperty("NameID", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                                ?.GetValue(p) as string;
                return new Directive.DirectivePropertyEntry
                {
                    Type = p.GetType().Name,
                    Label = PrettifyPropertyType(p.GetType().Name),
                    NameID = nameId,
                    Name = LocText(nameId, 0),
                    Description = LocText(nameId, 1),
                    Raw = p
                };
            }).ToArray();
            // Promote first property's name/description to the directive level so
            // consumers don't have to dig into Properties[0] to render a directive page.
            var first = d.Properties.FirstOrDefault(p => !string.IsNullOrEmpty(p?.NameID));
            if (first != null)
            {
                d.Name = first.Name;
                d.Description = first.Description;
            }
        }

        if (dd.AdditionalRewards.Properties != null)
        {
            d.AdditionalRewards = dd.AdditionalRewards.Properties
                .Where(lu => lu != null)
                .Select(BuildLevelUnlock)
                .ToArray();
        }

        return d;
    }

    /// <summary>
    /// Resolve a TextBlocks localization key to the requested block index, or
    /// null if the key is absent. Used to inline `Name` / `Description` /
    /// voiceline text alongside their IDs so consumers don't have to follow
    /// the `localization` map by hand.
    /// </summary>

    private static GlobalEventEntry BuildGlobalEvent(global::GlobalEvent ge)
    {
        var entry = new GlobalEventEntry
        {
            ID = GetPrivateField<string>(ge, "id") ?? ge.name,
            Subclass = ge.GetType().Name,
            EventID = GetPrivateField<string>(ge, "EventID"),
            StatID = GetPrivateField<string>(ge, "StatID"),
            Title = GetPrivateField<string>(ge, "Title"),
            Color = GetPrivateField<UnityEngine.Color>(ge, "color").ToString(),
            Deactivate = GetPrivateField<bool>(ge, "deactivate"),
            AreStatsRelative = GetPrivateField<bool>(ge, "areStatsRelative"),
            Completed = GetPrivateField<bool>(ge, "completed"),
            CompleteOnFullProgress = GetPrivateField<bool>(ge, "completeOnFullProgress"),
            MustParticipateToClaimRewards = GetPrivateField<bool>(ge, "mustParticipateToClaimRewards"),
            PreviewEndMission = GetPrivateField<bool>(ge, "previewEndMission"),
            EndDate = GetPrivateField<long>(ge, "endDate"),
            TotalRequiredProgress = GetPrivateField<long>(ge, "totalRequiredProgress"),
            ProgressOnMissionCompleted = GetPrivateField<int>(ge, "progressOnMissionCompleted"),
            ProgressOnMissionCompletedVisualMultiplier = GetPrivateField<float>(ge, "progressOnMissionCompletedVisualMultiplier"),
            ProgressDisplayMultiplier = GetPrivateField<float>(ge, "progressDisplayMultiplier"),
            OnStartRoachardLine = GetPrivateField<string>(ge, "OnStartRoachardLine"),
            OnStartTextLog = GetPrivateField<string>(ge, "OnStartTextLog"),
            OnEndTextLog = GetPrivateField<string>(ge, "OnEndTextLog"),
            RawData = ge
        };

        // Cross-reference end mission
        var endMission = GetPrivateField<Mission>(ge, "endMission");
        if (endMission != null) entry.EndMission = endMission.ID;
        var endMC = GetPrivateField<MissionContainer>(ge, "endMissionContainer");
        if (endMC != null) entry.EndMissionContainer = endMC.name;

        // ProgressStat[] structs
        var stats = GetPrivateField<System.Array>(ge, "progressStats");
        if (stats != null)
        {
            var list = new List<GlobalEventEntry.ProgressStatEntry>();
            foreach (var s in stats)
            {
                if (s == null) continue;
                list.Add(new GlobalEventEntry.ProgressStatEntry
                {
                    TitleID = GetPrivateField<string>(s, "TitleID"),
                    StatID = GetPrivateField<string>(s, "statID"),
                    Color = GetPrivateField<UnityEngine.Color>(s, "Color").ToString(),
                    DisplayType = GetPrivateField<object>(s, "DisplayType")?.ToString()
                });
            }
            entry.ProgressStats = list.ToArray();
        }

        // Rewards (LevelUnlockList)
        var rewards = GetPrivateField<LevelUnlockList>(ge, "rewards").Properties;
        if (rewards != null) entry.Rewards = rewards.Where(x => x != null).Select(BuildLevelUnlock).ToArray();

        // CorpContestGlobalEvent extension — corpData[] with WeaponID + Ticket resource.
        if (ge.GetType().Name == "CorpContestGlobalEvent")
        {
            var corp = GetPrivateField<System.Array>(ge, "corpData");
            if (corp != null)
            {
                var list = new List<GlobalEventEntry.CorpDataEntry>();
                foreach (var cd in corp)
                {
                    if (cd == null) continue;
                    var ticket = GetPrivateField<PlayerResource>(cd, "Ticket");
                    list.Add(new GlobalEventEntry.CorpDataEntry
                    {
                        WeaponID = GetPrivateField<string>(cd, "WeaponID"),
                        TicketResource = ticket?.ID
                    });
                }
                entry.CorpData = list.ToArray();
            }
        }

        return entry;
    }

    // ---- Rarity table -------------------------------------------------------

    // RarityData fields are [SerializeField] private — reflective per-field reads.
}
