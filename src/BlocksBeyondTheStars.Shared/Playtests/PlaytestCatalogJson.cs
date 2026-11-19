// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BlocksBeyondTheStars.Shared.Playtests;

/// <summary>How <see cref="PlaytestCatalogJson"/> treats what it reads.</summary>
public enum PlaytestReadMode
{
    /// <summary>For showing in the game: texts are cleaned and cut, lists cut to their bounds, entries that cannot
    /// be used (no id, an unknown question type, a command off the list) are left out. Never fails on content.</summary>
    Safe,

    /// <summary>For checking an author's task: everything is kept as written, so
    /// <see cref="PlaytestCatalogValidator"/> can name what is wrong.</summary>
    AsWritten,
}

/// <summary>
/// Reads and writes the playtest catalogue's JSON — one implementation for the inbox, the client and the sync tool's
/// tests. Reading is tolerant by design: an unknown field is ignored, a wrong-typed one reads as its default, and
/// only JSON that is not an object at all is refused. A newer catalogue therefore never breaks an older client.
/// </summary>
public static class PlaytestCatalogJson
{
    private static readonly JsonWriterOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonWriterOptions Indented = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true };

    /// <summary>Reads a whole catalogue. False (and an empty, disabled catalogue) when <paramref name="json"/> is
    /// not a JSON object. A catalogue of a newer <see cref="PlaytestCatalog.Schema"/> reads as disabled.</summary>
    public static bool TryReadCatalog(string? json, out PlaytestCatalog catalog, PlaytestReadMode mode = PlaytestReadMode.Safe)
    {
        catalog = new PlaytestCatalog { Enabled = false };
        if (!TryParseObject(json, out var doc))
        {
            return false;
        }

        using (doc)
        {
            var root = doc!.RootElement;
            catalog.Schema = Int(root, "schema", PlaytestLimits.Schema);
            catalog.Enabled = Bool(root, "enabled", true);
            if (mode == PlaytestReadMode.Safe && catalog.Schema > PlaytestLimits.Schema)
            {
                catalog.Enabled = false; // a format this build does not know: show nothing rather than guess
                return true;
            }

            foreach (var item in Array(root, "tasks"))
            {
                if (mode == PlaytestReadMode.Safe && catalog.Tasks.Count >= PlaytestLimits.MaxTasks)
                {
                    break;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var task = ReadTask(item, mode);
                if (mode == PlaytestReadMode.AsWritten || IsUsable(task))
                {
                    catalog.Tasks.Add(task);
                }
            }
        }

        return true;
    }

    /// <summary>Reads one task. False when <paramref name="json"/> is not a JSON object.</summary>
    public static bool TryReadTask(string? json, out PlaytestTask task, PlaytestReadMode mode = PlaytestReadMode.Safe)
    {
        task = new PlaytestTask();
        if (!TryParseObject(json, out var doc))
        {
            return false;
        }

        using (doc)
        {
            task = ReadTask(doc!.RootElement, mode);
        }

        return true;
    }

    /// <summary>Writes a whole catalogue (compact — what the client fetches).</summary>
    public static string WriteCatalog(PlaytestCatalog catalog)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Compact))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", catalog.Schema);
            w.WriteBoolean("enabled", catalog.Enabled);
            w.WriteStartArray("tasks");
            foreach (var task in catalog.Tasks)
            {
                WriteTask(w, task);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes one task — compact for storage, indented for an editing form.</summary>
    public static string WriteTask(PlaytestTask task, bool indented = false)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, indented ? Indented : Compact))
        {
            WriteTask(w, task);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // ---------------------------------------------------------------- reading

    private static bool IsUsable(PlaytestTask task) => task.Id.Length > 0 && task.Steps.Count > 0 && !task.Title.IsEmpty;

    private static PlaytestTask ReadTask(JsonElement o, PlaytestReadMode mode)
    {
        bool safe = mode == PlaytestReadMode.Safe;
        var task = new PlaytestTask
        {
            Id = Id(o, "id", safe),
            Rev = Int(o, "rev", 1),
            Enabled = Bool(o, "enabled", true),
            Sort = Int(o, "sort", 0),
            Area = Str(o, "area", PlaytestAreas.Ingame),
            Editor = Str(o, "editor", string.Empty),
            MinVersion = Str(o, "minVersion", string.Empty),
            MaxVersion = Str(o, "maxVersion", string.Empty),
            Title = Text(o, "title", PlaytestLimits.MaxTitleLength, safe),
            Intro = Text(o, "intro", PlaytestLimits.MaxTextLength, safe),
            Minutes = Int(o, "minutes", 0),
            Thanks = Text(o, "thanks", PlaytestLimits.MaxTextLength, safe),
            Retest = Bool(o, "retest", false),
        };
        if (safe)
        {
            task.Rev = Math.Max(1, task.Rev);
            task.Minutes = Math.Max(0, Math.Min(task.Minutes, 600));
            if (!PlaytestAreas.IsKnown(task.Area))
            {
                task.Area = PlaytestAreas.Ingame;
            }
        }

        if (o.TryGetProperty("world", out var world) && world.ValueKind == JsonValueKind.Object)
        {
            task.World = ReadWorld(world, safe);
        }

        ReadList(o, "steps", safe ? PlaytestLimits.MaxSteps : int.MaxValue, task.Steps, item => ReadStep(item, safe));
        ReadList(o, "questions", safe ? PlaytestLimits.MaxQuestions : int.MaxValue, task.Questions, item => ReadQuestion(item, safe));
        return task;
    }

    /// <summary>Reads the objects of an array into <paramref name="into"/>, at most <paramref name="max"/> of them;
    /// an entry <paramref name="read"/> answers null for is left out.</summary>
    private static void ReadList<T>(JsonElement o, string name, int max, List<T> into, Func<JsonElement, T?> read)
        where T : class
    {
        foreach (var item in Array(o, name))
        {
            if (into.Count >= max)
            {
                break;
            }

            if (item.ValueKind == JsonValueKind.Object && read(item) is { } entry)
            {
                into.Add(entry);
            }
        }
    }

    private static PlaytestStep? ReadStep(JsonElement o, bool safe)
    {
        var step = new PlaytestStep
        {
            Id = Id(o, "id", safe),
            Text = Text(o, "text", PlaytestLimits.MaxTextLength, safe),
        };
        if (safe && (step.Id.Length == 0 || step.Text.IsEmpty))
        {
            return null;
        }

        if (o.TryGetProperty("check", out var check) && check.ValueKind == JsonValueKind.Object)
        {
            var read = new PlaytestCheck
            {
                Event = Str(check, "event", string.Empty),
                Arg = Str(check, "arg", string.Empty),
                Required = Int(check, "required", 1),
            };
            if (safe)
            {
                read.Required = Math.Max(1, Math.Min(read.Required, PlaytestLimits.MaxRequired));
            }

            // A check this build cannot read (no event) leaves the step for the player to tick off.
            if (!safe || read.Event.Length > 0)
            {
                step.Check = read;
            }
        }

        return step;
    }

    private static PlaytestQuestion? ReadQuestion(JsonElement o, bool safe)
    {
        var question = new PlaytestQuestion
        {
            Id = Id(o, "id", safe),
            Type = Str(o, "type", PlaytestQuestionTypes.Stars),
            Text = Text(o, "text", PlaytestLimits.MaxTextLength, safe),
        };
        foreach (var item in Array(o, "options"))
        {
            if (safe && question.Options.Count >= PlaytestLimits.MaxOptions)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var option = new PlaytestOption
            {
                Id = Id(item, "id", safe),
                Text = Text(item, "text", PlaytestLimits.MaxTitleLength, safe),
            };
            if (!safe || (option.Id.Length > 0 && !option.Text.IsEmpty))
            {
                question.Options.Add(option);
            }
        }

        if (!safe)
        {
            return question;
        }

        // Every fixed question is mandatory, so one this build cannot ask must not exist for it.
        bool askable = question.Id.Length > 0 && !question.Text.IsEmpty && PlaytestQuestionTypes.IsKnown(question.Type)
                       && (question.Type != PlaytestQuestionTypes.Choice || question.Options.Count >= 2);
        return askable ? question : null;
    }

    private static PlaytestWorld ReadWorld(JsonElement o, bool safe)
    {
        var world = new PlaytestWorld
        {
            Seed = Long(o, "seed", 0),
            StartPlanet = Str(o, "startPlanet", string.Empty),
            Preset = Str(o, "preset", PlaytestWorldPresets.Standard),
            Tutorial = Str(o, "tutorial", PlaytestWorldPresets.TutorialOff),
            Story = Bool(o, "story", false),
        };
        if (safe)
        {
            if (!Contains(PlaytestWorldPresets.All, world.Preset))
            {
                world.Preset = PlaytestWorldPresets.Standard;
            }

            if (!Contains(PlaytestWorldPresets.TutorialRules, world.Tutorial))
            {
                world.Tutorial = PlaytestWorldPresets.TutorialOff;
            }
        }

        if (o.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in options.EnumerateObject())
            {
                if (safe && world.Options.Count >= PlaytestLimits.MaxWorldOptions)
                {
                    break;
                }

                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int value))
                {
                    world.Options[p.Name] = value;
                }
            }
        }

        ReadList(o, "commands", safe ? PlaytestLimits.MaxCommands : int.MaxValue, world.Commands, item => ReadCommand(item, safe));
        return world;
    }

    /// <summary>One start command; in safe mode null unless it is on the fixed list with a plain argument.</summary>
    private static PlaytestWorldCommand? ReadCommand(JsonElement item, bool safe)
    {
        var command = new PlaytestWorldCommand
        {
            Cmd = Str(item, "cmd", string.Empty),
            Arg = Str(item, "arg", string.Empty),
            Count = Int(item, "count", 0),
            X = Float(item, "x"),
            Y = Float(item, "y"),
            Z = Float(item, "z"),
        };
        return !safe || PlaytestWorldCommands.Problem(command) is null ? command : null;
    }

    private static bool TryParseObject(string? json, out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            doc = JsonDocument.Parse(json!, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return false;
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        doc.Dispose();
        doc = null;
        return false;
    }

    private static IEnumerable<JsonElement> Array(JsonElement o, string name)
    {
        if (o.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static string Str(JsonElement o, string name, string fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static int Int(JsonElement o, string name, int fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : fallback;

    private static long Long(JsonElement o, string name, long fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : fallback;

    private static float Float(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out float n) ? n : 0f;

    private static bool Bool(JsonElement o, string name, bool fallback)
        => o.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : fallback;

    /// <summary>An id: as written, or — in safe mode — empty unless it is a well-formed id.</summary>
    private static string Id(JsonElement o, string name, bool safe)
    {
        string id = Str(o, name, string.Empty);
        return !safe || PlaytestCatalogValidator.IsWellFormedId(id) ? id : string.Empty;
    }

    private static PlaytestText Text(JsonElement o, string name, int maxLength, bool safe)
    {
        var text = new PlaytestText();
        if (!o.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object)
        {
            return text;
        }

        text.De = Str(v, "de", string.Empty);
        text.En = Str(v, "en", string.Empty);
        if (safe)
        {
            text.De = PlaytestTextCleaner.Clean(text.De, maxLength);
            text.En = PlaytestTextCleaner.Clean(text.En, maxLength);
        }

        return text;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (string item in list)
        {
            if (string.Equals(item, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- writing

    private static void WriteTask(Utf8JsonWriter w, PlaytestTask task)
    {
        w.WriteStartObject();
        w.WriteString("id", task.Id);
        w.WriteNumber("rev", task.Rev);
        w.WriteBoolean("enabled", task.Enabled);
        w.WriteNumber("sort", task.Sort);
        w.WriteString("area", task.Area);
        WriteIfSet(w, "editor", task.Editor);
        WriteIfSet(w, "minVersion", task.MinVersion);
        WriteIfSet(w, "maxVersion", task.MaxVersion);
        WriteText(w, "title", task.Title);
        WriteText(w, "intro", task.Intro);
        if (task.Minutes > 0)
        {
            w.WriteNumber("minutes", task.Minutes);
        }

        if (task.World is { } world)
        {
            WriteWorld(w, world);
        }

        WriteSteps(w, task);
        WriteQuestions(w, task);
        if (!task.Thanks.IsEmpty)
        {
            WriteText(w, "thanks", task.Thanks);
        }

        if (task.Retest)
        {
            w.WriteBoolean("retest", true);
        }

        w.WriteEndObject();
    }

    private static void WriteSteps(Utf8JsonWriter w, PlaytestTask task)
    {
        w.WriteStartArray("steps");
        foreach (var step in task.Steps)
        {
            w.WriteStartObject();
            w.WriteString("id", step.Id);
            WriteText(w, "text", step.Text);
            if (step.Check is { } check)
            {
                w.WriteStartObject("check");
                w.WriteString("event", check.Event);
                WriteIfSet(w, "arg", check.Arg);
                if (check.Required != 1)
                {
                    w.WriteNumber("required", check.Required);
                }

                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteQuestions(Utf8JsonWriter w, PlaytestTask task)
    {
        w.WriteStartArray("questions");
        foreach (var question in task.Questions)
        {
            w.WriteStartObject();
            w.WriteString("id", question.Id);
            w.WriteString("type", question.Type);
            WriteText(w, "text", question.Text);
            if (question.Options.Count > 0)
            {
                w.WriteStartArray("options");
                foreach (var option in question.Options)
                {
                    w.WriteStartObject();
                    w.WriteString("id", option.Id);
                    WriteText(w, "text", option.Text);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteWorld(Utf8JsonWriter w, PlaytestWorld world)
    {
        w.WriteStartObject("world");
        if (world.Seed != 0)
        {
            w.WriteNumber("seed", world.Seed);
        }

        WriteIfSet(w, "startPlanet", world.StartPlanet);
        w.WriteString("preset", world.Preset);
        w.WriteString("tutorial", world.Tutorial);
        if (world.Story)
        {
            w.WriteBoolean("story", true);
        }

        if (world.Options.Count > 0)
        {
            w.WriteStartObject("options");
            foreach (var pair in world.Options)
            {
                w.WriteNumber(pair.Key, pair.Value);
            }

            w.WriteEndObject();
        }

        if (world.Commands.Count > 0)
        {
            w.WriteStartArray("commands");
            foreach (var command in world.Commands)
            {
                w.WriteStartObject();
                w.WriteString("cmd", command.Cmd);
                WriteIfSet(w, "arg", command.Arg);
                if (command.Count != 0)
                {
                    w.WriteNumber("count", command.Count);
                }

                if (command.Cmd == PlaytestWorldCommands.TeleportToLocation)
                {
                    w.WriteNumber("x", command.X);
                    w.WriteNumber("y", command.Y);
                    w.WriteNumber("z", command.Z);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        w.WriteEndObject();
    }

    private static void WriteText(Utf8JsonWriter w, string name, PlaytestText text)
    {
        w.WriteStartObject(name);
        w.WriteString("de", text.De);
        w.WriteString("en", text.En);
        w.WriteEndObject();
    }

    private static void WriteIfSet(Utf8JsonWriter w, string name, string value)
    {
        if (value.Length > 0)
        {
            w.WriteString(name, value);
        }
    }
}
