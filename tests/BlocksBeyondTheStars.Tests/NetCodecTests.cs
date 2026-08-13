// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Reflection;
using BlocksBeyondTheStars.Networking;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class NetCodecTests
{
    private const string MessageNamespace =
        "BlocksBeyondTheStars.Networking.Messages";

    [Fact]
    public void TopLevelMessages_HaveExactlyOneNetCodecTag()
    {
        var topLevelMessages = GetTopLevelMessageTypes();

        var missing = topLevelMessages
            .Where(type => !NetCodec.RegisteredMessageTags.ContainsKey(type))
            .OrderBy(type => type.FullName)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Top-level messages without a NetCodec tag: " +
            string.Join(", ", missing.Select(type => type.FullName)));
    }
    [Fact]
    public void EveryNetCodecTag_MapsToATopLevelMessage()
    {
        var topLevelMessages = GetTopLevelMessageTypes();

        var nonTopLevelRegistrations = NetCodec.RegisteredMessages
            .Where(entry => !topLevelMessages.Contains(entry.Value))
            .OrderBy(entry => entry.Key)
            .ToArray();

        Assert.True(
            nonTopLevelRegistrations.Length == 0,
            "NetCodec tags that do not map to top-level messages: " +
            string.Join(
                ", ",
                nonTopLevelRegistrations.Select(
                    entry => $"{entry.Key} -> {entry.Value.FullName}")));
    }

    private static HashSet<Type> GetTopLevelMessageTypes()
    {
        var messageTypes = GetMessageTypes();
        var referencedTypes = new HashSet<Type>();

        foreach (var messageType in messageTypes)
        {
            foreach (var property in messageType.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                CollectMessageTypes(property.PropertyType, referencedTypes);
            }
        }

        return messageTypes
            .Where(type => !referencedTypes.Contains(type))
            .ToHashSet();
    }

    private static Type[] GetMessageTypes()
    {
        return typeof(NetCodec).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                type.Namespace != null &&
                (type.Namespace == MessageNamespace ||
                 type.Namespace.StartsWith(
                     MessageNamespace + ".",
                     StringComparison.Ordinal)))
            .ToArray();
    }

    private static void CollectMessageTypes(
        Type type,
        HashSet<Type> referencedTypes)
    {
        if (type.IsArray)
        {
            CollectMessageTypes(type.GetElementType()!, referencedTypes);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                CollectMessageTypes(argument, referencedTypes);
            }
        }

        if (type.Namespace == MessageNamespace ||
            (type.Namespace?.StartsWith(
                MessageNamespace + ".",
                StringComparison.Ordinal) ?? false))
        {
            if (type.IsClass)
            {
                referencedTypes.Add(type);
            }
        }
    }
}
