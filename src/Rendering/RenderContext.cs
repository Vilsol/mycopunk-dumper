using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MycopunkDumper;

internal static partial class SkinRenderer
{
    private partial class RenderDriver
    {
        /// <summary>
        /// Per-skin render context shared across the FramesPerSkin loop. Holds
        /// the spawned pivot/instance, the camera framing parameters (computed
        /// once at identity rotation), and the original-materials snapshot used
        /// to reset state between frames.
        /// </summary>
        private class RotationTarget
        {
            public string Slug;        // sanitized output-folder suffix (e.g. "base", "Bloodmetal", "Hue", "Pixelated_1")
            public string Display;     // human-readable label for the status overlay
            public int PresetIdx;      // index into ctx.presetProps to force chance=1, -1 for none
            public int ModifierIdx;    // index into ctx.modifierProps to force chance=1, -1 for none
        }

        private class SkinRenderContext
        {
            public bool ready;
            public GameObject pivot;
            public GameObject instance;
            public IUpgradable instUpgradable;
            public float baseRotation;
            public Dictionary<Renderer, Material[]> originalMaterials;
            public List<GameObject> spawnedCrabs = new List<GameObject>();
            // Cached renderers — avoids walking the hierarchy twice per frame.
            public Renderer[] renderers;
            // True if the skin has any RNG-rolled property (chance < 1, hue,
            // trim, preset, color shift, etc.). When false, the skin's output
            // is identical for every seed — apply once on frame 0 and skip
            // the per-frame re-Apply for the remaining 179 frames.
            public bool seedAffectsOutput;
            // Tracks whether skin.Apply has run at least once on this context.
            public bool applied;
            // Rotation targets: one entry per 360° rotation we'll render for this
            // skin. Always starts with a "base" entry (nothing forced — only always-on
            // properties fire), followed by one entry per SkinUpgradeProperty_Preset
            // (preset isolated: chance=1 on that preset, 0 on all others), followed by
            // one entry per chance-gated non-preset modifier (modifier isolated: chance=1
            // on that mod, 0 on all other gated mods, all presets chance=0).
            public List<RotationTarget> rotationTargets;
            public List<SkinUpgradeProperty_Preset> presetProps; // direct refs; their `chance` field is what we toggle
            // Chance-gated non-preset modifiers — anything with a `public float chance` field
            // (covers RandStat subclasses + standalone ones like Pixelated/Contrast/Emissive/Negative/TrickOrTreat).
            // We cache the FieldInfo so per-rotation forcing is one virtual call, not a re-lookup.
            public List<(UpgradeProperty prop, System.Reflection.FieldInfo chanceField)> modifierProps;
            // Original chance values to restore on teardown — defensive against
            // SkinUpgradeProperty objects being shared across skins.
            public List<(UpgradeProperty prop, float originalChance)> chanceSnapshot;
            // Last rotation-target index applied — used to skip skin.Apply within a rotation.
            public int lastSegmentIdx;
            // Index into rotationTargets locking the current 360° rotation.
            public int forcedSegmentIdx;
            // Materials that the skin's Apply produced; we set _HueShift on these
            // each frame so the cycle hue without mutating the gear's shared assets.
            public List<Material> lastSkinMaterials;
            // Camera transform pinned per skin so the menu's per-frame camera
            // animation can't drift our framing.
            public Vector3 camPos;
            public Quaternion camRot;
            // Pinned instance/pivot transforms — re-applied every frame in case
            // anything (physics, particle simulation) shifts them under us.
            public Vector3 instanceLocalPos;
        }
    }
}
