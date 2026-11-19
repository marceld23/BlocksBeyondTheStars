// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The playtest tasks kept in the repository (<c>data-online/playtests/&lt;id&gt;.json</c>, the mirror of the inbox's
/// catalogue). A push of this folder changes what every player sees without a release, so each file is held to the
/// rules here: it reads as a task, the validator finds nothing — not even a warning, because an event name the game
/// does not raise is a typo that leaves a step open for ever — the game shows the texts exactly as written (nothing
/// cut or cleaned away), and a test world names a planet and items this content has.
/// </summary>
public sealed class PlaytestRepositoryTasksTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static string Folder() => Path.Combine(TestPaths.RepoRoot(), "data-online", "playtests");

    private static IEnumerable<string> Files()
        => Directory.Exists(Folder()) ? Directory.GetFiles(Folder(), "*.json").OrderBy(f => f, System.StringComparer.Ordinal) : Enumerable.Empty<string>();

    [Fact]
    public void EveryTaskFile_IsATask_NamedAfterItsId_WithoutAFinding()
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string path in Files())
        {
            string name = Path.GetFileName(path);
            if (!PlaytestCatalogJson.TryReadTask(File.ReadAllText(path), out var task, PlaytestReadMode.AsWritten))
            {
                problems.Add(name + ": does not read as a task");
                continue;
            }

            if (name != task.Id + ".json")
            {
                problems.Add(name + ": the file must be named after the task's id (" + task.Id + ".json)");
            }

            if (!ids.Add(task.Id))
            {
                problems.Add(name + ": the id " + task.Id + " is used twice");
            }

            problems.AddRange(PlaytestCatalogValidator.Validate(task).Select(p => name + ": " + p));
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void WhatTheGameShows_IsWhatWasWritten()
    {
        // The game reads in Safe mode: texts are cleaned and cut, entries it cannot use are dropped. A task in the
        // repository must come through that unchanged — otherwise a player sees less than the author wrote.
        foreach (string path in Files())
        {
            string json = File.ReadAllText(path);
            Assert.True(PlaytestCatalogJson.TryReadTask(json, out var written, PlaytestReadMode.AsWritten), path);
            Assert.True(PlaytestCatalogJson.TryReadTask(json, out var shown), path);

            Assert.Equal(PlaytestCatalogJson.WriteTask(written), PlaytestCatalogJson.WriteTask(shown));
        }
    }

    [Fact]
    public void ATestWorld_NamesAPlanetAndItemsThisContentHas()
    {
        var problems = new List<string>();
        foreach (string path in Files())
        {
            string name = Path.GetFileName(path);
            Assert.True(PlaytestCatalogJson.TryReadTask(File.ReadAllText(path), out var task, PlaytestReadMode.AsWritten), path);
            if (task.World is not { } world)
            {
                continue;
            }

            if (world.StartPlanet.Length > 0 && !Content.Planets.ContainsKey(world.StartPlanet))
            {
                problems.Add(name + ": unknown start planet " + world.StartPlanet);
            }

            foreach (var command in world.Commands)
            {
                if (command.Cmd == PlaytestWorldCommands.GiveItem && !Content.Items.ContainsKey(command.Arg))
                {
                    problems.Add(name + ": give_item names an unknown item " + command.Arg);
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
