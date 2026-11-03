using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace src.utils
{
    public static class PlayerCamera
    {
        public static CCSCustomPlayerCamera? Create(uint ownerKey, CCSPlayerPawn viewer, CustomCameraMode_t mode, Vector? position = null, QAngle? angles = null)
        {
            var camera = EntityManager.CreateTrackedPlayerCamera(ownerKey);
            if (camera == null) return null;

            camera.Pawn.Raw = viewer.EntityHandle.Raw;
            camera.DispatchSpawn();
            if (position != null || angles != null)
                camera.Teleport(position, angles, null);

            camera.Pawn.Raw = viewer.EntityHandle.Raw;
            camera.CameraMode = mode;
            NetStateFast.Changed(camera, "CCSCustomPlayerCamera", "m_hPawn");
            NetStateFast.Changed(camera, "CCSCustomPlayerCamera", "m_nCameraMode");
            return camera;
        }

        public static void Follow(CCSCustomPlayerCamera camera, CBaseEntity target, float distance, float height = 0f, bool followEyes = true)
        {
            camera.FollowEntity.Raw = target.EntityHandle.Raw;
            camera.CameraOffset.X = -distance;
            camera.CameraOffset.Y = 0f;
            camera.CameraOffset.Z = height;
            camera.FollowEyes = followEyes;
            camera.ClipCameraOffset = true;

            NetStateFast.Changed(camera, "CCSCustomPlayerCamera", "m_hFollowEntity");
            NetStateFast.Changed(camera, "CCSCustomPlayerCamera", "m_vecCameraOffset");
            NetStateFast.Changed(camera, "CCSCustomPlayerCamera", "m_bFollowEyes");
            NetStateFast.Changed(camera, "CCSCustomPlayerCamera", "m_bClipCameraOffset");
        }
    }
}
