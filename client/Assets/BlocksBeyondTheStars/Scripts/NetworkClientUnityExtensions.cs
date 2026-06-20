using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Unity-side convenience overloads that bridge <see cref="UnityEngine.Vector3"/> to the
    /// Unity-free <see cref="NetworkClient"/> (which lives in the <c>BlocksBeyondTheStars.Client.Core</c>
    /// assembly and speaks Shared's <see cref="Vector3f"/>). These keep every existing call-site
    /// (PlayerController, SpaceView, …) compiling unchanged: the instance methods take
    /// <see cref="Vector3f"/>, so a <c>UnityEngine.Vector3</c> argument resolves to the extension here.
    /// </summary>
    public static class NetworkClientUnityExtensions
    {
        private static Vector3f ToVec3f(Vector3 v) => new Vector3f(v.x, v.y, v.z);

        /// <summary>Reports the player's continuous position (unreliable position stream).</summary>
        public static void SendMove(this NetworkClient client, Vector3 pos, float yaw, float pitch)
            => client.SendMove(ToVec3f(pos), yaw, pitch);

        /// <summary>Uses a gadget aimed at a world point (e.g. deploy/translator/beacon).</summary>
        public static void SendUseGadget(this NetworkClient client, string gadgetKey, Vector3 target)
            => client.SendUseGadget(gadgetKey, ToVec3f(target));

        /// <summary>Reports the ship's position while flying in space (server-side collision).</summary>
        public static void SendShipMove(this NetworkClient client, Vector3 pos, float yaw = 0f)
            => client.SendShipMove(ToVec3f(pos), yaw);

        /// <summary>Deploys a hover speeder in front of the player.</summary>
        public static void SendDeploySpeeder(this NetworkClient client, Vector3 at)
            => client.SendDeploySpeeder(ToVec3f(at));
    }
}
