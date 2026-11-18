using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// A calibrated real monitor in MR tracking space. A fixed pool of these objects is authored under MrCore/Monitors;
    /// one is switched on per calibrated display. Origin at the lit-area centre, +z into the screen.
    /// </summary>
    public sealed class MonView : MonoBehaviour
    {
        public FxQuad outline;
        [NonSerialized] public MonitorDesc desc;
        [NonSerialized] public MonitorCalibration calib;
        [NonSerialized] public MonitorFrame frame;

        public Vector3 Origin => Conv.U(frame.origin);
        public Vector3 X => Conv.U(frame.x);
        public Vector3 Y => Conv.U(frame.y);
        public Vector3 Z => Conv.U(frame.z);
        public Vector3 ToWorld(float u, float v) => Conv.U(frame.ToWorld(ScreenMath.UVToLocal(frame.widthM, frame.heightM, u, v)));

        /// <summary>Gravity-aligned portal frame axes (same construction the PC side derives from the plan).</summary>
        public void GAxes(out Vector3 gx, out Vector3 gy, out Vector3 gz)
        {
            gy = Vector3.up;
            Vector3 z = Z; gz = z - gy * Vector3.Dot(z, gy);
            gz = gz.sqrMagnitude < 1e-6f ? Vector3.forward : gz.normalized;
            gx = Vector3.Cross(gy, gz);
        }
    }
}