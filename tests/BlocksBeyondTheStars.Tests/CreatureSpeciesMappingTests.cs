// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3971: a creature trait travels record → species → copy → wire. Each hop is ONE place now
/// (<see cref="AuthoredSpeciesMapping"/>, <see cref="CreatureSpecies.Clone"/>, <see cref="CreatureWireMapping"/>), and these
/// tests fail with the property's name when a new property is added to one side and forgotten on the next — the bug the
/// hand-written field lists kept producing (a companion that silently lost its gait, a pre-wave snapshot without heads).
/// </summary>
public sealed class CreatureSpeciesMappingTests
{
    [Fact]
    public void Clone_CopiesEveryPublicSettableProperty_AndEveryCollectionDeeply()
    {
        var src = new CreatureSpecies();
        int seed = 7;
        foreach (var prop in Settable(typeof(CreatureSpecies)))
        {
            prop.SetValue(src, Sample(prop.PropertyType, ++seed, prop.Name));
        }

        var clone = src.Clone();

        foreach (var prop in Settable(typeof(CreatureSpecies)))
        {
            object? a = prop.GetValue(src), b = prop.GetValue(clone);
            Assert.True(SameValue(a, b), $"CreatureSpecies.Clone drops {prop.Name} — a companion snapshot would silently lose it");
            if (a is IEnumerable and not string)
            {
                Assert.False(ReferenceEquals(a, b), $"CreatureSpecies.Clone shares the collection {prop.Name} with the live roster");
                foreach (var (x, y) in ((IEnumerable)a).Cast<object>().Zip(((IEnumerable)b!).Cast<object>()))
                {
                    Assert.True(x is string || x.GetType().IsValueType || !ReferenceEquals(x, y),
                        $"CreatureSpecies.Clone shares an element of {prop.Name} with the live roster");
                }
            }
        }
    }

    [Fact]
    public void AuthoredMapping_ReadsEveryRecordProperty_OrNamesItRecordOnly()
    {
        // A property counts as read when changing it alone changes the species it maps to — on the classic record, or on a
        // named biped (arms belong to the biped body, a fixed name needs a prefix).
        var baselines = new[]
        {
            new AuthoredCreature { Key = "probe" },
            new AuthoredCreature { Key = "probe", NamePrefix = "Leni", BodyPlan = CreatureBodyPlan.Biped },
        };

        foreach (var prop in Settable(typeof(AuthoredCreature)))
        {
            if (AuthoredSpeciesMapping.RecordOnly.Contains(prop.Name))
            {
                continue;
            }

            bool read = baselines.Any(baseline =>
            {
                var changed = CopyRecord(baseline);
                prop.SetValue(changed, Different(prop.PropertyType, prop.GetValue(baseline), prop.Name));
                return !SameSpecies(MapAuthored(baseline), MapAuthored(changed));
            });
            Assert.True(read, $"AuthoredCreature.{prop.Name} never reaches the species — read it in AuthoredSpeciesMapping.ToSpecies "
                + "or list it in AuthoredSpeciesMapping.RecordOnly with the place that uses it");
        }
    }

    [Fact]
    public void AuthoredMapping_RecordOnlyNamesRealProperties()
    {
        foreach (string name in AuthoredSpeciesMapping.RecordOnly)
        {
            Assert.NotNull(typeof(AuthoredCreature).GetProperty(name));
        }
    }

    [Fact]
    public void WireMapping_EverySpeciesPropertyReachesNetCreature_OrIsNamedServerOnly()
    {
        var baseline = new CreatureSpecies();
        var reference = Wire(baseline);
        foreach (var prop in Settable(typeof(CreatureSpecies)))
        {
            if (CreatureWireMapping.ServerOnly.Contains(prop.Name))
            {
                continue;
            }

            var changed = baseline.Clone();
            prop.SetValue(changed, Different(prop.PropertyType, prop.GetValue(baseline), prop.Name));
            Assert.False(SameObject(reference, Wire(changed)),
                $"CreatureSpecies.{prop.Name} never reaches NetCreature — write it in CreatureWireMapping or list it in "
                + "CreatureWireMapping.ServerOnly with the reason the client does not need it");
        }
    }

    [Fact]
    public void WireMapping_ServerOnlyNamesRealProperties()
    {
        foreach (string name in CreatureWireMapping.ServerOnly)
        {
            Assert.NotNull(typeof(CreatureSpecies).GetProperty(name));
        }
    }

    // ---------------- helpers ----------------

    private static NetCreature Wire(CreatureSpecies sp)
    {
        var n = new NetCreature();
        CreatureWireMapping.WriteSpecies(n, sp, ("world_type", 4711), animalSeed: 12345);
        CreatureWireMapping.WriteGiantSpecies(n, sp);
        return n;
    }

    private static CreatureSpecies MapAuthored(AuthoredCreature record)
        => AuthoredSpeciesMapping.ToSpecies(record, new PlanetType { Key = "probe" }, new System.Random(1), 0x5EEDL);

    private static AuthoredCreature CopyRecord(AuthoredCreature a)
    {
        var copy = new AuthoredCreature();
        foreach (var prop in Settable(typeof(AuthoredCreature)))
        {
            prop.SetValue(copy, prop.GetValue(a));
        }

        return copy;
    }

    private static PropertyInfo[] Settable(System.Type t)
        => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite).ToArray();

    private static bool SameSpecies(CreatureSpecies a, CreatureSpecies b) => SameObject(a, b);

    private static bool SameObject(object a, object b)
        => Settable(a.GetType()).All(p => SameValue(p.GetValue(a), p.GetValue(b)));

    private static bool SameValue(object? a, object? b)
    {
        if (a is IEnumerable ea and not string && b is IEnumerable eb and not string)
        {
            var xs = ea.Cast<object>().ToList();
            var ys = eb.Cast<object>().ToList();
            return xs.Count == ys.Count && xs.Zip(ys).All(p => SameValue(p.First, p.Second));
        }

        if (a is not null && b is not null && a.GetType() == b.GetType() && !a.GetType().IsValueType && a is not string)
        {
            return SameObject(a, b); // an element object (a coat variant) compares by its properties
        }

        return Equals(a, b);
    }

    /// <summary>A non-default sample value of <paramref name="t"/>.</summary>
    private static object Sample(System.Type t, int seed, string name)
    {
        if (t == typeof(string))
        {
            return "v" + seed;
        }

        if (t == typeof(int))
        {
            return seed;
        }

        if (t == typeof(float) || t == typeof(float?))
        {
            return seed + 0.5f;
        }

        if (t == typeof(bool))
        {
            return true;
        }

        if (t.IsEnum)
        {
            var values = t.GetEnumValues();
            return values.GetValue(seed % values.Length)!;
        }

        if (t.IsArray)
        {
            var arr = System.Array.CreateInstance(t.GetElementType()!, 2);
            arr.SetValue(Sample(t.GetElementType()!, seed, name), 0);
            arr.SetValue(Sample(t.GetElementType()!, seed + 1, name), 1);
            return arr;
        }

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
        {
            var list = (IList)System.Activator.CreateInstance(t)!;
            list.Add(Sample(t.GetGenericArguments()[0], seed, name));
            list.Add(Sample(t.GetGenericArguments()[0], seed + 1, name));
            return list;
        }

        if (t.IsClass && t.GetConstructor(System.Type.EmptyTypes) is not null)
        {
            object o = System.Activator.CreateInstance(t)!; // an element object: every settable property filled
            int s = seed * 31;
            foreach (var p in Settable(t))
            {
                p.SetValue(o, Sample(p.PropertyType, ++s, name + "." + p.Name));
            }

            return o;
        }

        throw new System.InvalidOperationException($"unhandled property type {t} on {name} — teach the completeness test it");
    }

    /// <summary>A value of <paramref name="t"/> that differs from <paramref name="current"/> (and stays inside every clamp the
    /// mappings apply).</summary>
    private static object? Different(System.Type t, object? current, string name)
    {
        if (t == typeof(string))
        {
            return (string?)current + "x";
        }

        if (t == typeof(int))
        {
            return (int)current! + 2;
        }

        if (t == typeof(float))
        {
            return (float)current! + 0.25f;
        }

        if (t == typeof(float?))
        {
            return ((float?)current ?? 1f) + 7f;
        }

        if (t == typeof(bool))
        {
            return !(bool)current!;
        }

        if (t.IsEnum)
        {
            var values = t.GetEnumValues();
            int i = System.Array.IndexOf(values, current);
            return values.GetValue((i + 1) % values.Length);
        }

        if (current is IEnumerable e && !e.Cast<object>().Any())
        {
            return Sample(t, 3, name); // an empty collection becomes a filled one
        }

        throw new System.InvalidOperationException($"unhandled property type {t} on {name} — teach the completeness test it");
    }
}
