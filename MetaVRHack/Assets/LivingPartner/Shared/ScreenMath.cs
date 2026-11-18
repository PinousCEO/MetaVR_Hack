using System;
using System.Collections.Generic;
using System.Numerics;

namespace LivingPartner.Shared
{
    [Serializable]
    public struct RectI
    {
        public int x, y, w, h;
        public RectI(int x, int y, int w, int h) { this.x = x; this.y = y; this.w = w; this.h = h; }
        public int Right => x + w;
        public int Bottom => y + h;
        public bool Contains(float px, float py) => px >= x && px < x + w && py >= y && py < y + h;
        public bool IsEmpty => w <= 0 || h <= 0;
        public static RectI Intersect(RectI a, RectI b)
        {
            int x0 = Math.Max(a.x, b.x), y0 = Math.Max(a.y, b.y), x1 = Math.Min(a.Right, b.Right), y1 = Math.Min(a.Bottom, b.Bottom);
            return (x1 <= x0 || y1 <= y0) ? new RectI(x0, y0, 0, 0) : new RectI(x0, y0, x1 - x0, y1 - y0);
        }
        public override string ToString() => $"[{x},{y} {w}x{h}]";
    }

    /// <summary>A Windows display as reported by the Companion Host (physical pixels, virtual-desktop coordinates; may be negative).</summary>
    [Serializable]
    public sealed class MonitorDesc
    {
        public string id = "";          // GDI device name, e.g. \\.\DISPLAY2
        public string stableId = "";    // device interface path / EDID-derived id when available
        public string label = "";
        public int index;
        public bool primary;
        public RectI bounds;            // physical pixels
        public RectI work;              // work area (excludes taskbar), physical pixels
        public float dpiScale = 1f;     // effective DPI / 96
        public int rotation;            // 0, 90, 180, 270 (as displayed)
        public float physWidthM, physHeightM; // from EDID (already rotated to match bounds), 0 if unknown
        public int refreshHz;
    }

    /// <summary>Manual calibration of the lit display area in MR space (bezel excluded: corners are the on-screen pixel corners).</summary>
    [Serializable]
    public sealed class MonitorCalibration
    {
        public string monitorId = "";
        public string stableId = "";
        public V3 tl, tr, bl, br;        // MR tracking-space positions of the lit corners
        public float widthM, heightM;
        public int pixelW, pixelH;       // display mode when calibrated; a change invalidates the calibration
        public string spaceEpoch = "";   // id of the MR tracking space at calibration time; a change requires verification
        public long savedUnixMs;
        public float residualM;          // 4th-corner planarity/rectangularity residual measured at calibration
        public bool simulated;           // true when produced by the desktop MR simulator (never real evidence)
        public bool hasDesk;             // desk surface height measured in the same session
        public float deskY;              // tracking-space height of the desk surface the partner walks on
    }

    /// <summary>Orthonormal frame of a display in some world: origin at centre, +x right, +y up, +z INTO the screen.</summary>
    public struct MonitorFrame
    {
        public Vector3 origin, x, y, z;
        public float widthM, heightM;

        public static MonitorFrame FromCorners(Vector3 tl, Vector3 tr, Vector3 bl, Vector3 br)
        {
            var f = new MonitorFrame();
            f.origin = (tl + tr + bl + br) * 0.25f;
            Vector3 xr = (tr - tl) + (br - bl);
            Vector3 yr = (tl - bl) + (tr - br);
            f.widthM = xr.Length() * 0.5f; f.heightM = yr.Length() * 0.5f;
            f.x = Vector3.Normalize(xr);
            f.y = Vector3.Normalize(yr - f.x * Vector3.Dot(yr, f.x));
            f.z = Vector3.Cross(f.x, f.y);
            return f;
        }

        public static MonitorFrame FromCalibration(MonitorCalibration c) => FromCorners(c.tl, c.tr, c.bl, c.br);

        public Vector3 ToLocal(Vector3 world) { var d = world - origin; return new Vector3(Vector3.Dot(d, x), Vector3.Dot(d, y), Vector3.Dot(d, z)); }
        public Vector3 ToWorld(Vector3 local) => origin + x * local.X + y * local.Y + z * local.Z;
        public Vector3 DirToLocal(Vector3 w) => new Vector3(Vector3.Dot(w, x), Vector3.Dot(w, y), Vector3.Dot(w, z));
        public Vector3 DirToWorld(Vector3 l) => x * l.X + y * l.Y + z * l.Z;
        public Quaternion Rotation => M.FromAxes(x, y, z);

        /// <summary>Residual (m) between the measured 4 corners and the fitted rectangle. Large values mean a bad calibration.</summary>
        public float Residual(Vector3 tl, Vector3 tr, Vector3 bl, Vector3 br)
        {
            float hw = widthM * 0.5f, hh = heightM * 0.5f;
            float r = 0;
            r = Math.Max(r, Vector3.Distance(tl, ToWorld(new Vector3(-hw, hh, 0))));
            r = Math.Max(r, Vector3.Distance(tr, ToWorld(new Vector3(hw, hh, 0))));
            r = Math.Max(r, Vector3.Distance(bl, ToWorld(new Vector3(-hw, -hh, 0))));
            r = Math.Max(r, Vector3.Distance(br, ToWorld(new Vector3(hw, -hh, 0))));
            return r;
        }

        /// <summary>Ray/plane hit. Returns false when the ray is parallel or points away. local = hit in the display frame.</summary>
        public bool Raycast(Vector3 rayOrigin, Vector3 rayDir, out Vector3 local, out float distance)
        {
            local = default; distance = 0;
            float denom = Vector3.Dot(rayDir, z);
            if (Math.Abs(denom) < 1e-6f) return false;
            float t = Vector3.Dot(origin - rayOrigin, z) / denom;
            if (t <= 0) return false;
            distance = t;
            local = ToLocal(rayOrigin + rayDir * t);
            return true;
        }
    }

    /// <summary>
    /// The chain MR world -> display plane (m) -> normalised UV -> Windows physical pixel -> window client coordinates.
    /// Each step is a separate, individually tested function; nothing assumes that the physical arrangement of
    /// the monitors matches the Windows arrangement.
    /// </summary>
    public static class ScreenMath
    {
        public const float DefaultMetersPerPixel = 0.000277f; // ~24" 1080p; only used when neither calibration nor EDID is available

        public static void LocalToUV(float widthM, float heightM, Vector3 local, out float u, out float v)
        {
            u = local.X / widthM + 0.5f; v = 0.5f - local.Y / heightM;
        }
        public static Vector3 UVToLocal(float widthM, float heightM, float u, float v) => new Vector3((u - 0.5f) * widthM, (0.5f - v) * heightM, 0);

        public static void UVToPixel(MonitorDesc m, float u, float v, out float px, out float py)
        {
            px = m.bounds.x + u * m.bounds.w; py = m.bounds.y + v * m.bounds.h;
        }
        public static void PixelToUV(MonitorDesc m, float px, float py, out float u, out float v)
        {
            u = (px - m.bounds.x) / m.bounds.w; v = (py - m.bounds.y) / m.bounds.h;
        }

        /// <summary>Physical size of the lit area: calibration first, then EDID, then a labelled default.</summary>
        public static void PhysicalSize(MonitorDesc m, MonitorCalibration c, out float wM, out float hM, out string source)
        {
            if (c != null && c.widthM > 0.01f && c.heightM > 0.01f) { wM = c.widthM; hM = c.heightM; source = "calibration"; return; }
            if (m.physWidthM > 0.01f && m.physHeightM > 0.01f) { wM = m.physWidthM; hM = m.physHeightM; source = "edid"; return; }
            wM = m.bounds.w * DefaultMetersPerPixel; hM = m.bounds.h * DefaultMetersPerPixel; source = "default";
        }

        /// <summary>Pixel (virtual desktop) -> display-frame metres (origin at the display centre, +y up).</summary>
        public static Vector3 PixelToLocal(MonitorDesc m, float wM, float hM, float px, float py)
        {
            PixelToUV(m, px, py, out float u, out float v);
            return UVToLocal(wM, hM, u, v);
        }
        public static void LocalToPixel(MonitorDesc m, float wM, float hM, Vector3 local, out float px, out float py)
        {
            LocalToUV(wM, hM, local, out float u, out float v);
            UVToPixel(m, u, v, out px, out py);
        }

        public static void PixelToClient(RectI client, float px, float py, out float cx, out float cy, out float nu, out float nv)
        {
            cx = px - client.x; cy = py - client.y;
            nu = client.w > 0 ? cx / client.w : 0; nv = client.h > 0 ? cy / client.h : 0;
        }
        public static void ClientToPixel(RectI client, float nu, float nv, out float px, out float py)
        {
            px = client.x + nu * client.w; py = client.y + nv * client.h;
        }

        public static MonitorDesc FindMonitor(IList<MonitorDesc> mons, float px, float py)
        {
            if (mons == null) return null;
            foreach (var m in mons) if (m.bounds.Contains(px, py)) return m;
            return null;
        }
        public static MonitorDesc FindMonitor(IList<MonitorDesc> mons, string id)
        {
            if (mons == null) return null;
            foreach (var m in mons) if (m.id == id) return m;
            return null;
        }

        /// <summary>
        /// Rotation taking gravity-frame G vectors (Y = world up, Z = horizontal into-screen) into the display frame.
        /// worldUpLocal is the world up direction expressed in the display frame (for an upright monitor: (0,1,0)).
        /// </summary>
        public static Quaternion GToMonitor(Vector3 worldUpLocal)
        {
            Vector3 yg = M.SafeNormalize(worldUpLocal, Vector3.UnitY);
            Vector3 zm = Vector3.UnitZ;
            Vector3 zg = M.SafeNormalize(zm - yg * Vector3.Dot(zm, yg), Vector3.UnitZ);
            Vector3 xg = Vector3.Cross(yg, zg);
            return M.FromAxes(xg, yg, zg);
        }

        /// <summary>
        /// Validates a requested portal pixel: it must be inside the allowed rectangle with enough clearance for the body.
        /// Returns the nearest valid pixel and whether it had to move (the caller must show and confirm an adjustment).
        /// </summary>
        public static bool ClampPortal(RectI allowed, float clearancePx, float px, float py, out float outX, out float outY)
        {
            float minX = allowed.x + clearancePx, maxX = allowed.Right - clearancePx;
            float minY = allowed.y + clearancePx, maxY = allowed.Bottom - clearancePx;
            if (maxX < minX) { minX = maxX = allowed.x + allowed.w * 0.5f; }
            if (maxY < minY) { minY = maxY = allowed.y + allowed.h * 0.5f; }
            outX = M.Clamp(px, minX, maxX); outY = M.Clamp(py, minY, maxY);
            return Math.Abs(outX - px) > 0.5f || Math.Abs(outY - py) > 0.5f;
        }

        /// <summary>Off-axis frustum extents at the near plane for an eye at eyeLocal (display frame, z &lt; 0) looking through a rectangle on z = 0.</summary>
        public static void OffAxis(Vector3 eyeLocal, float left, float right, float bottom, float top, float near,
            out float l, out float r, out float b, out float t)
        {
            float d = Math.Max(0.02f, -eyeLocal.Z);
            float k = near / d;
            l = (left - eyeLocal.X) * k; r = (right - eyeLocal.X) * k; b = (bottom - eyeLocal.Y) * k; t = (top - eyeLocal.Y) * k;
        }
    }

    /// <summary>NTP-style clock offset estimator: hostTime ~= localTime + Offset. Keeps the lowest-RTT samples.</summary>
    public sealed class ClockSync
    {
        readonly List<(double rtt, double offset)> _samples = new List<(double, double)>();
        public double Offset { get; private set; }
        public double Rtt { get; private set; } = double.MaxValue;
        public int Count => _samples.Count;
        public bool Synced => _samples.Count >= 3;

        public void AddSample(double localSend, double hostTime, double localRecv)
        {
            double rtt = localRecv - localSend;
            if (rtt < 0) return;
            double offset = hostTime - (localSend + rtt * 0.5);
            _samples.Add((rtt, offset));
            if (_samples.Count > 24) _samples.RemoveAt(0);
            double best = double.MaxValue; double sum = 0; int n = 0;
            foreach (var s in _samples) if (s.rtt < best) best = s.rtt;
            foreach (var s in _samples) if (s.rtt <= best * 1.5 + 0.002) { sum += s.offset; n++; }
            Offset = sum / Math.Max(1, n); Rtt = best;
        }

        public double ToHost(double local) => local + Offset;
    }
}
