// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Story;

public static partial class StoryPackValidator
{
    /// <summary>The pack-level cutscene bindings: the intro, the beats' cutscenes and the finale's stages (#2478).</summary>
    private static void ValidateCutsceneBindings(Run run)
    {
        run.Cutscene("introCutscene", run.Def.IntroCutscene);
        foreach (var (beat, i) in run.Def.Beats.Select((b, i) => (b, i)))
        {
            run.Cutscene($"beats[{i}].cutscene", beat.Cutscene);
        }

        foreach (var binding in run.Def.Finale?.Cutscenes ?? new Dictionary<string, string>())
        {
            // #2478: the finale code plays exactly these stages — another name would never play.
            if (!StoryFinaleStages.All.Contains(binding.Key))
            {
                run.Error($"finale.cutscenes.{binding.Key}", $"unknown finale stage '{binding.Key}' ({string.Join(", ", StoryFinaleStages.All)})");
            }

            run.Cutscene($"finale.cutscenes.{binding.Key}", binding.Value);
        }
    }

    /// <summary>The cutscene rules (synthesis §6.2/§6.3): unique key-safe ids (no built-in <c>__</c> id), a known tier, at
    /// least one shot, known camera types, anchors (a <c>diorama</c> shows <c>core</c> or <c>star_map</c>, #2477), caption
    /// styles and grades, positive shot lengths with captions and cues
    /// inside them, every caption key in English and German within its style's budget, the tier rules (a T1/T2 never
    /// takes the camera; only a T3/T4 that takes it may hold the world or freeze the controls), <c>chainNext</c> references
    /// that resolve, a chapter that exists, and (when the context can tell) existing music and sound cues.</summary>
    private static void ValidateCutscenes(Run run)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int ci = 0; ci < run.Def.Cutscenes.Count; ci++)
        {
            var cut = run.Def.Cutscenes[ci];
            string cp = $"cutscenes[{ci}]";
            if (string.IsNullOrEmpty(cut.Id) || !IsKeySafeId(cut.Id, allowDot: true) || !ids.Add(cut.Id))
            {
                run.Error(cp, $"cutscene id '{cut.Id}' is empty, not key-safe or used twice");
            }
            else if (StoryCutsceneIds.IsBuiltIn(cut.Id))
            {
                run.Error(cp, $"'{cut.Id}': ids starting with '{StoryCutsceneIds.BuiltInPrefix}' are built in");
            }

            if (!CutsceneTiers.IsValid(cut.Tier))
            {
                run.Error($"{cp}.tier", $"unknown tier {cut.Tier} (1 banner, 2 card, 3 scene, 4 set piece)");
            }

            if (!string.IsNullOrEmpty(cut.Chapter) && !run.ChapterIds.Contains(cut.Chapter))
            {
                run.Error($"{cp}.chapter", $"the pack has no chapter '{cut.Chapter}'");
            }

            if (!CutsceneArgSources.IsValid(cut.Arg))
            {
                run.Error($"{cp}.arg", $"unknown argument source '{cut.Arg}' ({string.Join(", ", CutsceneArgSources.All)}, or none for the bound chapter)");
            }

            foreach (var (line, i) in (cut.ReplaceLineKeys ?? new List<string>()).Select((l, i) => (l, i)))
            {
                run.Key($"{cp}.replaceLineKeys[{i}]", line, 0, 0);
            }

            if (!string.IsNullOrEmpty(cut.Music) && run.Context.MusicExists is { } musicExists && !musicExists(cut.Music))
            {
                run.Error($"{cp}.music", $"music track '{cut.Music}' does not exist in client/Music");
            }

            var shots = cut.Shots ?? new List<CutsceneShot>();
            if (shots.Count == 0)
            {
                run.Error($"{cp}.shots", "a cutscene needs at least one shot");
            }

            for (int si = 0; si < shots.Count; si++)
            {
                ValidateCutsceneShot(run, cut, shots[si], $"{cp}.shots[{si}]");
            }

            // ---- Options: the tier rules of synthesis §5.3 "Stufen" ----
            ValidateCutsceneOptions(run, cut, cp);
        }
    }

    /// <summary>One shot of a cutscene: its camera and anchor, its length, its grade, its sound cues and its captions.</summary>
    private static void ValidateCutsceneShot(Run run, CutsceneDefinition cut, CutsceneShot shot, string sp)
    {
        string camera = shot.Camera?.Type ?? string.Empty;
        string anchor = shot.Camera?.Anchor ?? string.Empty;
        if (!CutsceneCameras.All.Contains(camera))
        {
            run.Error($"{sp}.camera.type", $"unknown camera '{camera}' ({string.Join(", ", CutsceneCameras.All)})");
        }
        else if (cut.Tier < CutsceneTiers.Scene && CutsceneCameras.TakesCamera(camera))
        {
            run.Error($"{sp}.camera.type", $"a T{cut.Tier} {(cut.Tier == CutsceneTiers.Banner ? "banner" : "card")} never takes the camera (player_view or overlay)");
        }

        if (anchor.Length > 0 && !CutsceneAnchors.All.Contains(anchor))
        {
            run.Error($"{sp}.camera.anchor", $"unknown anchor '{anchor}' ({string.Join(", ", CutsceneAnchors.All)})");
        }
        else if (camera == CutsceneCameras.Diorama && !CutsceneCameras.DioramaAnchors.Contains(anchor))
        {
            // #2477: a diorama is a small synthetic stand-in for what its anchor names.
            run.Error($"{sp}.camera.anchor", $"a diorama shows {string.Join(" or ", CutsceneCameras.DioramaAnchors)} (found '{anchor}')");
        }
        else if (anchor == CutsceneAnchors.Players && camera != CutsceneCameras.Chart)
        {
            // #2478: the other players are names on the star chart — no other camera can frame them.
            run.Error($"{sp}.camera.anchor", "the players anchor labels the star chart — it needs a chart camera");
        }

        if (!(shot.Duration > 0f) || shot.Duration > ShotMaxSeconds)
        {
            run.Error($"{sp}.duration", $"a shot lasts more than 0 and at most {ShotMaxSeconds:0} seconds (found {shot.Duration})");
        }

        if (!CutsceneGrades.All.Contains(shot.Grade ?? string.Empty))
        {
            run.Error($"{sp}.grade", $"unknown grade '{shot.Grade}' ({string.Join(", ", CutsceneGrades.All)})");
        }

        foreach (var (sfx, xi) in (shot.Sfx ?? new List<CutsceneSfx>()).Select((x, i) => (x, i)))
        {
            string xp = $"{sp}.sfx[{xi}]";
            if (string.IsNullOrEmpty(sfx.Cue))
            {
                run.Error(xp, "a sound cue needs a clip name");
            }
            else if (run.Context.SoundExists is { } soundExists && !soundExists(sfx.Cue))
            {
                run.Error(xp, $"sound '{sfx.Cue}' does not exist in client/Assets/Resources/audio");
            }

            if (sfx.At < 0f || (shot.Duration > 0f && sfx.At >= shot.Duration))
            {
                run.Error($"{xp}.at", $"the cue at {sfx.At} s lies outside the {shot.Duration} s shot");
            }
        }

        foreach (var (caption, ki) in (shot.Captions ?? new List<CutsceneCaption>()).Select((c, i) => (c, i)))
        {
            ValidateCaption(run, shot, caption, $"{sp}.captions[{ki}]");
        }
    }

    /// <summary>One caption of a shot: its style, its key within the style's budget, its kicker, its speaker and its timing.</summary>
    private static void ValidateCaption(Run run, CutsceneShot shot, CutsceneCaption caption, string kp)
    {
        string style = caption.Style ?? string.Empty;
        if (!CutsceneCaptionStyles.All.Contains(style))
        {
            run.Error($"{kp}.style", $"unknown caption style '{style}' ({string.Join(", ", CutsceneCaptionStyles.All)})");
        }

        if (string.IsNullOrEmpty(caption.Key))
        {
            run.Error(kp, "a caption needs a key");
        }

        var (maxEn, maxDe) = style switch
        {
            CutsceneCaptionStyles.TitleCard => (TitleCardMaxEn, TitleCardMaxDe),
            CutsceneCaptionStyles.ReadingFrame => (ReadingFrameMaxEn, ReadingFrameMaxDe),
            CutsceneCaptionStyles.Credits => (0, 0),
            _ => (CaptionLineMaxEn, CaptionLineMaxDe),
        };
        run.Key($"{kp}.key", caption.Key, maxEn, maxDe);
        if (!string.IsNullOrEmpty(caption.KickerKey))
        {
            // #2478: a reading frame shows its kicker as the heading (the recap, the epilogue's title).
            if (style != CutsceneCaptionStyles.TitleCard && style != CutsceneCaptionStyles.ReadingFrame)
            {
                run.Warn($"{kp}.kickerKey", "only a title_card or a reading_frame shows a kicker");
            }

            run.Key($"{kp}.kickerKey", caption.KickerKey, TitleCardMaxEn, TitleCardMaxDe);
        }

        if (!string.IsNullOrEmpty(caption.Speaker) && style != CutsceneCaptionStyles.Speaker)
        {
            run.Warn($"{kp}.speaker", "only a speaker caption names a speaker");
        }
        else if (!string.IsNullOrEmpty(caption.Speaker) && run.Def.FindSpeaker(caption.Speaker) is null)
        {
            // #2478: an unknown speaker id still plays (it reads as its own name), but it is most likely a typo.
            run.Warn($"{kp}.speaker", $"speaker '{caption.Speaker}' is neither the pack's narrator nor one of its speakers");
        }

        if (caption.At < 0f || (shot.Duration > 0f && caption.At >= shot.Duration))
        {
            run.Error($"{kp}.at", $"the caption at {caption.At} s lies outside the {shot.Duration} s shot");
        }

        if (caption.Hold < 0f)
        {
            run.Error($"{kp}.hold", "hold must not be negative (0 = until the shot ends)");
        }
    }

    /// <summary>A cutscene's options: the tier rules, the chain and the lines given back on a skip.</summary>
    private static void ValidateCutsceneOptions(Run run, CutsceneDefinition cut, string cp)
    {
        var options = cut.Options ?? new CutsceneOptions();
        bool takeover = cut.Tier >= CutsceneTiers.Scene && cut.TakesCamera;
        if (options.HoldWorld == true && !takeover)
        {
            run.Error($"{cp}.options.holdWorld", "only a T3/T4 scene that takes the camera may hold the world");
        }

        if (options.LockInput == true && !takeover)
        {
            run.Error($"{cp}.options.lockInput", "only a T3/T4 scene that takes the camera may freeze the controls");
        }

        if (options.MinDisplay < 0f)
        {
            run.Error($"{cp}.options.minDisplay", "minDisplay must not be negative");
        }

        foreach (var (next, ni) in (options.ChainNext ?? new List<string>()).Select((n, i) => (n, i)))
        {
            CheckChainReference(run, cut, $"{cp}.options.chainNext[{ni}]", next);
        }

        // #2474: a line given back on a skip must be one the scene takes out of the panel in the first place.
        foreach (var (line, li) in (options.ReturnOnSkip ?? new List<string>()).Select((k, i) => (k, i)))
        {
            if (string.IsNullOrEmpty(line) || !(cut.ReplaceLineKeys ?? new List<string>()).Contains(line!))
            {
                run.Error($"{cp}.options.returnOnSkip[{li}]", $"'{line}' is not one of the scene's replaceLineKeys");
            }
        }
    }

    /// <summary>A <c>chainNext</c> entry: <c>__recap</c>, an id of the same pack, or <c>&lt;pack&gt;/&lt;id&gt;</c> — an
    /// error when it names nothing, a warning when it names a pack that is not installed (packs are optional).</summary>
    private static void CheckChainReference(Run run, CutsceneDefinition from, string path, string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            run.Error(path, "an empty chain reference");
            return;
        }

        if (reference == StoryCutsceneIds.Recap)
        {
            return;
        }

        int slash = reference!.IndexOf(StoryCutsceneIds.PackSeparator);
        if (slash < 0)
        {
            if (reference == from.Id)
            {
                run.Error(path, "a cutscene cannot chain to itself");
            }
            else if (run.Def.FindCutscene(reference) is null || reference.IndexOf(StoryCutsceneIds.InstanceSeparator) >= 0)
            {
                run.Error(path, $"cutscene '{reference}' is not defined in the pack");
            }

            return;
        }

        string pack = reference.Substring(0, slash);
        string id = reference.Substring(slash + 1);
        if (string.Equals(pack, run.Def.Id, StringComparison.OrdinalIgnoreCase))
        {
            CheckChainReference(run, from, path, id);
            return;
        }

        StoryDefinition? other = run.Context.Packs is { } packs && packs.TryGetValue(pack, out var p) ? p : null;
        if (other is null && run.Context.Packs is not null)
        {
            run.Warn(path, $"pack '{pack}' is not installed — the chain applies only when it is");
            return;
        }

        if (id == StoryCutsceneIds.Recap)
        {
            return; // that pack's recap card
        }

        bool exists = (other?.FindCutscene(id) is not null) || (run.Context.CutsceneExists?.Invoke(pack, id) ?? false);
        if (!exists && (other is not null || run.Context.CutsceneExists is not null))
        {
            run.Error(path, $"pack '{pack}' has no cutscene '{id}'");
        }
    }
}
