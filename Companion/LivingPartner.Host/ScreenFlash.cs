using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// Lights whole displays with one plain colour for a moment, so the headset's camera can see exactly where each one
/// is (which colour says which display). The headset asks for it and keeps asking a few times a second; the moment it
/// stops asking - a display has been found, the headset was taken off, the cable came out - the colour is gone. A
/// click or a key on a lit display takes all of them away at once and keeps them away for a while: nothing here may
/// ever stay in the way of the PC (corner markers once did, for twenty minutes).
/// </summary>
public sealed class ScreenFlash
{
    sealed class FlashForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; return cp; }      // no-activate, tool window, topmost
        }
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr Topmost = new(-1);

    Thread _thread; Control _marshal;
    readonly Dictionary<string, FlashForm> _forms = new();
    string _shown = "";
    public double SuppressedUntil = -1;
    public bool Active => _shown.Length > 0;

    void Ensure()
    {
        if (_thread != null) return;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _marshal = new Control(); _ = _marshal.Handle;
            ready.Set();
            Application.Run();
        }) { IsBackground = true, Name = "lp-flash" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(3000);
    }

    /// <summary>Shows exactly these displays lit (an empty list: none). Called from the core thread.</summary>
    public void Show(CalibFlash want, List<MonitorDesc> monitors)
    {
        var items = new List<(string id, RectI b, Color c)>();
        if (want != null && HostClock.Now >= SuppressedUntil)
            foreach (var it in want.items)
            {
                var m = ScreenMath.FindMonitor(monitors, it.monitorId);
                if (m != null) items.Add((m.id, m.bounds, Color.FromArgb(Math.Clamp(it.r, 0, 255), Math.Clamp(it.g, 0, 255), Math.Clamp(it.b, 0, 255))));
            }
        string key = string.Join(" ", items.Select(i => $"{i.id.Replace("\\\\.\\", "")}:{i.c.R:x2}{i.c.G:x2}{i.c.B:x2}"));
        if (key == _shown) return;
        if (items.Count == 0 && _thread == null) { _shown = ""; return; }
        Log.Info(key.Length > 0 ? "screen finding: lit " + key : "screen finding: colours off");
        _shown = key;
        Ensure();
        try { _marshal.BeginInvoke(new Action(() => Apply(items))); } catch (Exception e) { Log.Warn("screen finding: " + e.Message); }
    }

    void Apply(List<(string id, RectI b, Color c)> items)
    {
        foreach (var id in _forms.Keys.ToList())
            if (!items.Any(i => i.id == id)) { _forms[id].Close(); _forms[id].Dispose(); _forms.Remove(id); }
        foreach (var it in items)
        {
            if (!_forms.TryGetValue(it.id, out var f))
            {
                f = new FlashForm { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, TopMost = true, Cursor = Cursors.Hand };
                f.Click += (_, _) => Dismiss("clicked");
                f.KeyDown += (_, _) => Dismiss("key");
                _forms[it.id] = f;
            }
            f.BackColor = it.c;
            f.Show();
            SetWindowPos(f.Handle, Topmost, it.b.x, it.b.y, it.b.w, it.b.h, 0x0010 | 0x0040);      // no-activate, show
        }
    }

    /// <summary>The user wants the PC back: everything goes, and nothing is lit again for a while.</summary>
    void Dismiss(string why)
    {
        SuppressedUntil = HostClock.Now + 20;
        Log.Info($"screen finding: colours taken away by the user ({why}); none for 20 s");
        foreach (var f in _forms.Values) { f.Close(); f.Dispose(); }
        _forms.Clear();
    }
}
