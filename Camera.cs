using System;
using System.Collections.Specialized;
using System.Reflection;
using Y5Lib;


namespace Y5Coop
{
    internal unsafe static class Camera
    {
        public static float Height = 1.5f;
        public static float MinFOV = 0.6f;
        public static float MaxFOV = 1f;
        public static float MinFOVDistance = 6.5f;
        public static float MaxFOVDistance = 15f;
        public static float smoothSpeed = 5f;

        public static float MinFollowDistance = 5;
        public static float MaxFollowDistance = 12f;
        public static float MinFollowOffset = 3.5f;
        public static float MaxFollowOffset = 13f;

        public delegate void CCameraFreeUpdate(IntPtr cam);

        internal static CCameraFreeUpdate m_updateFuncOrig;
        public static void CCameraFree_Update(IntPtr cam)
        {
            if (Mod.IsChase())
            {
                m_updateFuncOrig(cam);
                return;
            }

            CameraBase camera = new CameraBase() { Pointer = cam };

            if (Mod.CoopPlayer != null)
            {
                Fighter p1 = ActionFighterManager.GetFighter(0);

                Vector3 center = (p1.Position + Mod.CoopPlayer.Position) / 2f;
                Vector3 focusPos = center + new Vector3(0, Height, 0); // + camera.Matrix.UpDirection * 1.3f;  // new Vector3(0, 1.3f, 0);
                camera.FocusPosition = Vector3.Lerp(camera.FocusPosition, focusPos, 0.05f);

                float distance = Vector3.Distance(p1.Position, Mod.CoopPlayer.Position);
                float t = ModMath.InverseLerp(MinFOVDistance, MaxFOVDistance, distance);
                float targetFOV = ModMath.Lerp(MinFOV, MaxFOV, t);

                float t2 = ModMath.InverseLerp(MinFollowOffset, MaxFollowOffset, distance);
                float targetDist = ModMath.Lerp(MinFollowOffset, MaxFollowOffset, t);

                Vector3 dir = ModMath.Normalize((Mod.CoopPlayer.Position - p1.Position));

                Matrix4x4 mtx = ActionFighterManager.GetFighter(0).HumanMotion.Matrix;

                Vector3 followPos = mtx.Position;
                followPos += new Vector3(0, 2, 0);
                followPos -= dir * targetDist;

                camera.Position = Vector3.Lerp(camera.Position, followPos, 1f * ActionManager.UnscaledDeltaTime);

                camera.FieldOfView = ModMath.Lerp(camera.FieldOfView, targetFOV, ActionManager.DeltaTime * smoothSpeed);

                // camera.Position = camPos;

                //if (Vector3.Distance(camera.Position, focusPos) >= FollowDistance)
                //camera.Position = Vector3.Lerp(camera.Position, focusPos + new Vector3(0, 0.65f, 0), 1f * ActionManager.UnscaledDeltaTime);
            }
            else
            {
                m_updateFuncOrig(cam);
            }


            if (SequenceManager.MissionID == 408)
            {
            }
            else
            {
                //m_updateFuncOrig(cam);
            }
        }
    }
}
