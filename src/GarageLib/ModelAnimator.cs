using System;
using System.Collections.Generic;
using MoonCleaners;
using UnityEngine;

namespace GarageLib
{
    /// <summary>
    /// Moves a data vehicle's model with the base vehicle under it: nodes with a <c>spin</c> prop (wheel radius in metres,
    /// negative to reverse) roll with ground speed, nodes with a <c>brush</c> prop turn while the vehicle is cleaning.
    /// Speed comes from position change, so it works whatever drives the base.
    /// </summary>
    public class ModelAnimator : MonoBehaviour
    {
        public ModelAnimator(IntPtr ptr) : base(ptr) { }

        internal MoonPrototypeVehicle Vehicle;
        internal readonly List<(Transform node, float radius)> Wheels = new();
        internal readonly List<Transform> Brushes = new();
        Vector3 last;
        bool primed;
        bool wasCleaning;
        internal float CameraScale = 1f;
        float camSet = float.NaN;
        const float BrushDegPerSec = 540f;

        void Update()
        {
            try
            {
                float dt = Time.deltaTime;
                if (dt <= 0f) return;
                var t = transform;
                var pos = t.position;
                float speed = primed ? Vector3.Dot(pos - last, t.forward) / dt : 0f;
                last = pos;
                primed = true;
                if (Mathf.Abs(speed) > 60f) speed = 0f;   // a teleport (garage handover), not driving
                // The game sets the chase distance for the base vehicle. Whenever it writes a new value, scale that one;
                // a value we wrote ourselves is left alone so the scale never compounds.
                var walker = Vehicle != null ? Vehicle.Owner : null;
                if (CameraScale > 1.01f && walker != null && walker.mountedVehicle != null && walker.mountedVehicle.Pointer == Vehicle.Pointer)
                {
                    float d = walker.vehicleCameraDistance;
                    if (float.IsNaN(camSet) || Mathf.Abs(d - camSet) > 0.001f)
                    {
                        camSet = d * CameraScale;
                        walker.vehicleCameraDistance = camSet;
                        CoreLog.Once("cam-" + name, $"'{name}': chase camera {d:F1} -> {camSet:F1} m", 6);
                    }
                }
                else camSet = float.NaN;
                foreach (var (node, r) in Wheels)
                    if (node != null) node.Rotate(Vector3.right, speed / r * Mathf.Rad2Deg * dt, Space.Self);
                bool cleaning = Vehicle != null && Vehicle.Cleaning;
                if (cleaning && !wasCleaning)
                    CoreLog.Debug($"'{name}': cleaning, spec width {Vehicle.Spec?.width:F2}, CleaningWidth {Vehicle.CleaningWidth:F2}, suction {Vehicle.Suction} (width share {Vehicle.SuctionWidthShare:F2}, speed share {Vehicle.SuctionSpeedShare:F2})");
                wasCleaning = cleaning;
                if (Brushes.Count > 0 && cleaning)
                    foreach (var b in Brushes)
                        if (b != null) b.Rotate(Vector3.up, BrushDegPerSec * dt, Space.Self);
            }
            catch (Exception e) { CoreLog.Once("animator", "ModelAnimator.Update: " + e, 2); enabled = false; }
        }
    }
}
