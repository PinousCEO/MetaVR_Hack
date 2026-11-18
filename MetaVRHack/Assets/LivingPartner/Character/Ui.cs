using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// Shared look: one palette and a few helpers for driving motion-graphics quads. The objects themselves are
    /// authored in scenes / prefabs (see the editor's LPSceneBuilder); runtime code only drives them.
    /// </summary>
    public static class Ui
    {
        /// <summary>
        /// The product palette: a picture-book look. Cream paper surfaces with a soft brown ink for text and outlines, and
        /// a handful of gentle pastels. Nothing glows and nothing is saturated.
        /// </summary>
        public static class Pal
        {
            // Storybook paper: cream panels with a brown ink line, pastel pills, ink lettering - the partner's own look.
            // (A night-glass palette was tried for an evening: it had nothing to do with a small friend in a raincoat.)
            public static readonly Color Ink = new Color(0.24f, 0.17f, 0.15f);
            public static readonly Color PanelTop = new Color(1.00f, 0.985f, 0.945f, 0.98f);
            public static readonly Color PanelBottom = new Color(0.985f, 0.945f, 0.875f, 0.98f);
            public static readonly Color Violet = new Color(0.76f, 0.71f, 0.92f);     // lavender
            public static readonly Color Pink = new Color(0.96f, 0.71f, 0.72f);       // rose
            public static readonly Color Orange = new Color(0.99f, 0.83f, 0.56f);     // honey
            public static readonly Color Cyan = new Color(0.66f, 0.84f, 0.93f);       // sky
            public static readonly Color Mint = new Color(0.71f, 0.86f, 0.72f);       // sage
            public static readonly Color Text = new Color(0.24f, 0.17f, 0.15f);
            public static readonly Color TextDim = new Color(0.36f, 0.28f, 0.25f);
            public static readonly Color Shadow = new Color(0.30f, 0.22f, 0.20f, 0.30f);
            public static readonly Color Rain = new Color(0.84f, 0.92f, 1.00f, 0.80f);
        }
        public static void SetFxColor(FxQuad q, Color c) => q.SetColor(c);
        public static void SetFxColors(FxQuad q, Color c, Color c2) => q.SetColors(c, c2);
        public static void SetFxParam(FxQuad q, Vector4 p) => q.SetParam(p);
        public static void SetFxPhase(FxQuad q, float phase) => q.SetPhase(phase);

        /// <summary>Sizes a mode-2 panel quad and keeps its corners round whatever its aspect.</summary>
        public static void SizePanel(FxQuad q, float width, float height, float cornerMeters, float rimMeters = 0f)
        {
            q.transform.localScale = new Vector3(width, height, 1);
            float half = Mathf.Max(1e-5f, Mathf.Min(width, height) * 0.5f);
            q.SetParam(new Vector4(Mathf.Clamp01(cornerMeters / half), rimMeters / half, width / Mathf.Max(1e-5f, height), 0));
        }

        /// <summary>Overshooting ease for things that pop in (0..1 -> 0..1 with a small bounce).</summary>
        public static float Pop(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public static string Arg(string name, string fallback = null)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }
        public static bool HasArg(string name)
        {
            foreach (var s in System.Environment.GetCommandLineArgs()) if (s == name) return true;
            return false;
        }
    }
}
