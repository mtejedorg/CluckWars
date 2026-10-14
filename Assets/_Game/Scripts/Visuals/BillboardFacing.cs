using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Facing direction for world-space billboards (nameplates, status badges, HP bars, floating text).
    /// </summary>
    /// <remarks>
    /// Under the orthographic match camera every view ray is parallel to the camera's forward, so a
    /// billboard must face along that forward to sit flat on screen. Aiming at the camera POSITION
    /// (the perspective recipe) tilts billboards away from screen centre by atan(offset / distance),
    /// and Phase 8 A6 halved the camera distance to 15 m to bring the view inside the shadow
    /// distance, which roughly doubled that tilt. Perspective cameras keep the position recipe.
    /// </remarks>
    public static class BillboardFacing
    {
        /// <summary>Direction a billboard at <paramref name="position"/> should look along
        /// (pass to <c>Quaternion.LookRotation(dir, Vector3.up)</c>).</summary>
        public static Vector3 Direction(Camera cam, Vector3 position)
        {
            var t = cam.transform;
            return cam.orthographic ? t.forward : position - t.position;
        }
    }
}
