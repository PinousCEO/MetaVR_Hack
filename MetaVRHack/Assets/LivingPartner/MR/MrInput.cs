using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>One tracked hand, in MR tracking space. Filled by the Quest hand-tracking adapter or by the desktop simulator.</summary>
    public struct HandState
    {
        public bool tracked;
        public Vector3 palmPos;       // centre of the palm
        public Vector3 palmNormal;    // direction the palm faces
        public Vector3 palmForward;   // wrist -> fingers
        public Vector3 indexTip;
        public Vector3 thumbTip;
        public Vector3 middleTip, ringTip, littleTip;   // (zero when the platform does not report them)
        public Vector3 wrist;
        public Vector3 rayOrigin;     // stabilised pointing ray
        public Vector3 rayDir;
        // What the pointing ray was made from: 0 = nothing known (a scripted hand), 1 = along an index finger that is
        // held out by itself (knuckle through tip), 2 = the system's pointer pose of a relaxed hand, 3 = shoulder
        // through the knuckle (when the system gives no pointer pose), 4 = the simulator's mouse / scripted ray.
        public int rayKind;
        public bool pinch;            // index pinch held
        public bool pinchDown;        // went down this frame
        public float pinchStrength;
        public bool fingersKnown;     // the platform reports the fingers one by one
        public int fingers;           // stretched out: bit 0 thumb, 1 index, 2 middle, 4 = bit 2 ... (1 << n), n = 0 thumb .. 4 little finger
        public bool palmUp => tracked && Vector3.Dot(palmNormal, Vector3.up) > 0.65f;
    }

    /// <summary>
    /// One picture from the headset's own camera, with what is needed to turn a pixel into a ray in the room.
    /// The pixels are 4 bytes each (red, green, blue, alpha), the TOP row first.
    /// </summary>
    public struct CameraShot
    {
        public int width, height;
        public double timestamp;             // provider capture time, zero if unavailable
        public float fx, fy, cx, cy;          // pixels; cy counted from the top row
        public Vector3 position;              // the camera in tracking space when the picture was taken
        public Quaternion rotation;           // x right, y up, z out of the lens
    }

    public interface IMrCameraPreview
    {
        bool CameraPreview(ref byte[] rgba, out CameraShot shot);
        bool HasCameraPermission { get; }
    }

    /// <summary>A board-sized image retains finger detail across a living-room TV.</summary>
    public interface IMrBoardCamera
    {
        bool BoardCameraFrame(ref byte[] rgba, out CameraShot shot);
    }

    /// <summary>Platform input abstraction for the MR experience (hands first; no controller required).</summary>
    public interface IMrInput
    {
        bool Simulated { get; }
        bool HeadTracked { get; }
        Vector3 HeadPos { get; }
        Quaternion HeadRot { get; }
        HandState Left { get; }
        HandState Right { get; }
        /// <summary>Changes whenever the tracking space is recentred/reset; saved calibrations must then be re-verified.</summary>
        string SpaceEpoch { get; }
        void Tick(float dt);
        /// <summary>Latest observation from the real passthrough camera, if the platform provides one (null otherwise).</summary>
        LivingPartner.Shared.Observation PollObservation();
        /// <summary>User setting: stop/start reading camera frames (seeing passthrough is unaffected).</summary>
        void SetCameraEnabled(bool on);
        /// <summary>
        /// Casts a ray against the REAL surroundings (the headset's depth sensing). False when the platform cannot, when
        /// the user has not allowed access to spatial data, or when nothing is hit within maxDistance.
        /// </summary>
        bool RaycastEnvironment(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point);
        /// <summary>
        /// The newest picture from the headset's camera (the buffer is reused between calls). False when the platform
        /// has no camera to read, when the user has not allowed it, or when no picture has arrived yet.
        /// </summary>
        bool CameraFrame(ref byte[] rgba, out CameraShot shot);
        /// <summary>One short line about the input devices for the debug overlay (hands found, camera state...).</summary>
        string Diagnostics { get; }
    }
}
