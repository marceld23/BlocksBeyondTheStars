using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// In-editor (headless, no graphics) unit tests for the Unity-free <see cref="ClientWorld"/> view.
    /// The same logic is also exercised against the real server in the dotnet-test suite; this confirms it
    /// behaves identically inside the Unity runtime/Mono. See docs/developer/CLIENT_TESTING.md.
    /// </summary>
    public sealed class ClientWorldEditModeTests
    {
        private static ushort[] FilledChunk(ushort id)
        {
            var blocks = new ushort[WorldConstants.BlocksPerChunk];
            for (int i = 0; i < blocks.Length; i++)
            {
                blocks[i] = id;
            }

            return blocks;
        }

        [Test]
        public void StoreChunk_ThenGetBlock_ReturnsTheStoredId()
        {
            var world = new ClientWorld();
            world.StoreChunk(new ChunkCoord(0, 0, 0), FilledChunk(1));

            Assert.AreEqual(1, world.GetBlock(2, 2, 2).Value);
        }

        [Test]
        public void ApplyBlockChange_MutatesALoadedChunk()
        {
            var world = new ClientWorld();
            world.StoreChunk(new ChunkCoord(0, 0, 0), FilledChunk(1));

            bool applied = world.ApplyBlockChange(2, 2, 2, 0, out _);

            Assert.IsTrue(applied);
            Assert.IsTrue(world.GetBlock(2, 2, 2).IsAir);
        }

        [Test]
        public void ApplyBlockChange_OnUnloadedChunk_ReturnsFalse()
        {
            var world = new ClientWorld();
            // Nothing stored at this far-away cell, so the change can't apply.
            Assert.IsFalse(world.ApplyBlockChange(9000, 90, 9000, 5, out _));
        }
    }
}
