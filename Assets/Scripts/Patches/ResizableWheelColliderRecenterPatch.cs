using Assets.Scripts.CustomWheelCollider;
using HarmonyLib;
using UnityEngine;

namespace Patches
{
    /// <summary>
    ///     Harmony postfix patch for <see cref="ResizableWheelColliderNew.RecalculateFrameState" />.
    ///     On a floating-origin recenter the tire track renderer shifts its stored geometry and
    ///     _previousSegmentPosition by positionDelta (TireTrackRenderer.MoveAllSections), and the
    ///     collider shifts _positionLastFrame, but LastGroundPoint is left in the old frame until
    ///     the next physics raycast refreshes it. LandingGearTracks.Update feeds LastGroundPoint
    ///     into the renderer transform every frame, so for the intervening frame the renderer
    ///     compares a shifted _previousSegmentPosition against an unshifted contact point and lays
    ///     one spurious segment of length ~positionDelta along the travel direction. That shows up
    ///     as a skid mark spawning in front of the vehicle.
    ///     Shifting LastGroundPoint by the same delta keeps it frame-consistent and removes the
    ///     artifact regardless of script execution order. The value is harmlessly overwritten by
    ///     the next grounded raycast, and it is not used for force or velocity math.
    /// </summary>
    [HarmonyPatch(typeof(ResizableWheelColliderNew), nameof(ResizableWheelColliderNew.RecalculateFrameState))]
    internal static class ResizableWheelColliderRecenterPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ResizableWheelColliderNew __instance, Vector3 positionDelta)
        {
            __instance.LastGroundPoint += positionDelta;
        }
    }
}
