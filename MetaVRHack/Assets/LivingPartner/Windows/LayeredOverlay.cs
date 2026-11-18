using System;
using System.Collections.Generic;
using LivingPartner.Shared;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingPartner.Windows
{
    /// <summary>
    /// Native compositing host for the desktop character. Unity renders the character off-screen into a small render
    /// target; the pixels are pushed into a borderless, per-pixel-alpha Win32 layered window with UpdateLayeredWindow.
    ///  - Real per-pixel alpha (anti-aliased edges), no chroma key, no black rectangle.
    ///  - WS_EX_TRANSPARENT | WS_EX_NOACTIVATE: the window never takes focus and all mouse input falls through to
    ///    whatever is underneath, so normal clicking/typing/scrolling is unaffected.
    ///  - Position, size and pixels are submitted in ONE call, so the window never shows stale content at a new place.
    ///  - Only a character-sized buffer is rendered, never the whole virtual desktop.
    /// Nothing is injected into other applications.
    /// </summary>
    public sealed unsafe class LayeredOverlay : IDisposable
    {
        public IntPtr Hwnd { get; private set; }
        public int BufferW { get; private set; }
        public int BufferH { get; private set; }
        public RenderTexture Color { get; private set; }
        public bool Shown { get; private set; }
        public int FramesPresented { get; private set; }
        public RectI LastPresentedRect { get; private set; }
        public float LastReadbackMs { get; private set; }
        public int Pending => _pending;
        public int Requests { get; private set; }
        public int ReadbackErrors { get; private set; }
        public int EmptyFrames { get; private set; }
        public bool FlipY = false;
        public IntPtr InsertAfter = Win32.HWND_TOPMOST;     // z-order target used on the next present
        public bool ZOrderDirty = true;
        /// <summary>
        /// PARKED (the start argument -lp-park, for tests on a PC somebody is working at): every overlay window is put
        /// this many pixels to the side of where it belongs - far off all displays - and nothing else changes: what is
        /// rendered, read back and believed about the monitors is the same. BlendInto gives what would have been seen.
        /// </summary>
        public static int ParkX;
        /// <summary>Where the frame that was presented last belongs on the desktop (its whole buffer, not only what is visible of it).</summary>
        public RectI LastDest { get; private set; }

        RenderTexture _encoded;
        Material _encodeMat;
        IntPtr _screenDc, _memDc, _dib, _oldBmp, _bits;
        int _pending;

        struct Frame { public RectI dest; public RectI visible; public double t; public int id; }
        readonly Queue<Frame> _frames = new Queue<Frame>();
        int _frameId;

        public LayeredOverlay(int w, int h, Material encodeMaterial)
        {
            BufferW = w; BufferH = h;
            Win32.EnsureDpiAware();
            uint ex = Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST;
            Hwnd = Win32.CreateWindowEx(ex, "Static", "LivingPartnerOverlay", Win32.WS_POPUP, 0, 0, w, h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (Hwnd == IntPtr.Zero) throw new Exception("CreateWindowEx failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());

            _screenDc = Win32.GetDC(IntPtr.Zero);
            _memDc = Win32.CreateCompatibleDC(_screenDc);
            var bmi = new Win32.BITMAPINFO();
            bmi.bmiHeader.biSize = sizeof(Win32.BITMAPINFOHEADER);
            bmi.bmiHeader.biWidth = w; bmi.bmiHeader.biHeight = h;   // positive = bottom-up, matches Unity's readback row order
            bmi.bmiHeader.biPlanes = 1; bmi.bmiHeader.biBitCount = 32; bmi.bmiHeader.biCompression = 0;
            _dib = Win32.CreateDIBSection(_memDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero) throw new Exception("CreateDIBSection failed");
            _oldBmp = Win32.SelectObject(_memDc, _dib);

            Color = new RenderTexture(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4, sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear })
            { name = "LP_OverlayColor", filterMode = FilterMode.Bilinear };
            Color.Create();
            _encoded = new RenderTexture(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0) { msaaSamples = 1, sRGB = false }) { name = "LP_OverlayEncoded" };
            _encoded.Create();
            _encodeMat = encodeMaterial;      // Art/Materials/LPEncode.mat, assigned on PcApp in the scene
            _encodeMat.SetFloat("_SrcIsLinear", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);
        }

        /// <summary>
        /// Queues the current contents of the colour target for presentation at dest (physical pixels, virtual desktop),
        /// cropped to clip (monitor bounds / workspace window). Call after the overlay camera has rendered this frame.
        /// </summary>
        public void Present(RectI dest, RectI clip)
        {
            if (Hwnd == IntPtr.Zero || _pending > 3) return;
            _encodeMat.SetFloat("_FlipY", FlipY ? 1f : 0f);
            Graphics.Blit(Color, _encoded, _encodeMat);
            var f = new Frame { dest = dest, visible = RectI.Intersect(dest, clip), t = Time.realtimeSinceStartupAsDouble, id = ++_frameId };
            _frames.Enqueue(f);
            _pending++; Requests++;
            AsyncGPUReadback.Request(_encoded, 0, TextureFormat.BGRA32, OnReadback);
        }

        void OnReadback(AsyncGPUReadbackRequest req)
        {
            _pending--;
            if (_frames.Count == 0) return;
            var f = _frames.Dequeue();
            if (req.hasError || Hwnd == IntPtr.Zero) { ReadbackErrors++; return; }
            var data = req.GetData<byte>();
            if (data.Length < BufferW * BufferH * 4) return;
            UnsafeUtility.MemCpy((void*)_bits, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data), (long)BufferW * BufferH * 4);
            LastReadbackMs = (float)((Time.realtimeSinceStartupAsDouble - f.t) * 1000.0);
            if (f.visible.IsEmpty) { EmptyFrames++; Hide(); return; }

            var dst = new Win32.POINT(f.visible.x + ParkX, f.visible.y);
            var size = new Win32.SIZE(f.visible.w, f.visible.h);
            var src = new Win32.POINT(f.visible.x - f.dest.x, f.visible.y - f.dest.y);
            var blend = new Win32.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            Win32.UpdateLayeredWindow(Hwnd, _screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA);
            LastPresentedRect = f.visible; LastDest = f.dest;
            FramesPresented++;
            if (!Shown || ZOrderDirty)
            {
                Win32.SetWindowPos(Hwnd, InsertAfter, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW | Win32.SWP_NOOWNERZORDER);
                Shown = true; ZOrderDirty = false;
            }
        }

        /// <summary>
        /// Lays the frame that was presented last over a picture of a part of the desktop: px is w x h pixels, its rows
        /// from the BOTTOM up (as a Texture2D has them), and shows the desktop area `area` (physical pixels). Nothing
        /// is done when this window is not shown. (The buffer is BGRA with the colour already multiplied by alpha.)
        /// </summary>
        public bool BlendInto(Color32[] px, int w, int h, RectI area)
        {
            if (!Shown || _bits == IntPtr.Zero || px == null || px.Length < w * h) return false;
            RectI vis = RectI.Intersect(LastPresentedRect, area);
            if (vis.IsEmpty) return false;
            byte* bits = (byte*)_bits;
            for (int sy = vis.y; sy < vis.y + vis.h; sy++)
            {
                int by = BufferH - 1 - (sy - LastDest.y);            // (the buffer's rows run from the bottom up)
                if (by < 0 || by >= BufferH) continue;
                int row = (h - 1 - (sy - area.y)) * w;
                for (int sx = vis.x; sx < vis.x + vis.w; sx++)
                {
                    int bx = sx - LastDest.x;
                    if (bx < 0 || bx >= BufferW) continue;
                    byte* p = bits + ((long)by * BufferW + bx) * 4;
                    int a = p[3];
                    if (a == 0) continue;
                    int i = row + (sx - area.x), k = 255 - a;
                    Color32 d = px[i];
                    px[i] = new Color32((byte)Math.Min(255, p[2] + d.r * k / 255), (byte)Math.Min(255, p[1] + d.g * k / 255), (byte)Math.Min(255, p[0] + d.b * k / 255), 255);
                }
            }
            return true;
        }

        public void Hide()
        {
            if (Hwnd != IntPtr.Zero && Shown) { Win32.ShowWindow(Hwnd, Win32.SW_HIDE); Shown = false; }
        }

        public void Dispose()
        {
            try { AsyncGPUReadback.WaitAllRequests(); } catch { }
            if (Hwnd != IntPtr.Zero) { Win32.DestroyWindow(Hwnd); Hwnd = IntPtr.Zero; }
            if (_memDc != IntPtr.Zero) { Win32.SelectObject(_memDc, _oldBmp); Win32.DeleteDC(_memDc); _memDc = IntPtr.Zero; }
            if (_dib != IntPtr.Zero) { Win32.DeleteObject(_dib); _dib = IntPtr.Zero; }
            if (_screenDc != IntPtr.Zero) { Win32.ReleaseDC(IntPtr.Zero, _screenDc); _screenDc = IntPtr.Zero; }
            if (Color != null) { Color.Release(); UnityEngine.Object.Destroy(Color); }
            if (_encoded != null) { _encoded.Release(); UnityEngine.Object.Destroy(_encoded); }
        }
    }
}
