using System;
using System.Collections.Generic;
using LivingPartner.Character;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The project's own typeface, "LPStory": a rounded single-stroke alphabet drawn here from scratch. Every glyph is a
    /// handful of lines and arcs (the centre lines of a round-nib pen); this builder rasterises them into an atlas and
    /// saves a Unity Font asset per weight (Art/Fonts/LPStory-Regular, LPStory-Bold). No font file is downloaded or
    /// embedded - the definition below IS the font. Authoring time only; the players just use the saved assets.
    ///
    /// Design grid (units): baseline 0, x-height 5, cap height 7, ascender 7.5, descender -2.2; 10 units = 1 em.
    /// Metrics are written on a 64-per-em grid so a TextMesh sized for the old 64 px font keeps its size.
    /// </summary>
    public static class StoryFontBuilder
    {
        public const string FontDir = "Assets/LivingPartner/Art/Fonts";
        const float U = 6.4f;            // font "pixels" per design unit (64 per em)
        const int K = 2;                 // atlas pixels per font pixel
        const int AtlasW = 2048, AtlasH = 1024;
        const float LineSpacing = 74f;   // font pixels
        const float SpaceAdvance = 2.9f;

        sealed class Glyph { public float w; public List<Vector2[]> strokes = new List<Vector2[]>(); }

        public sealed class Result { public Font regular, bold; public int glyphs; }

        public static Result Build()
        {
            if (!AssetDatabase.IsValidFolder(FontDir)) AssetDatabase.CreateFolder("Assets/LivingPartner/Art", "Fonts");
            var glyphs = Define();
            for (char c = ' '; c <= '~'; c++) if (!glyphs.ContainsKey(c)) throw new Exception($"LPStory: no glyph for '{c}'");
            foreach (char c in StoryGlyphs.Extra) if (!glyphs.ContainsKey(c)) throw new Exception($"LPStory: no glyph for U+{(int)c:X4}");
            return new Result
            {
                regular = BuildWeight("LPStory-Regular", glyphs, 0.44f, 1.75f),
                bold = BuildWeight("LPStory-Bold", glyphs, 0.64f, 1.95f),
                glyphs = glyphs.Count,
            };
        }

        // ------------------------------------------------------------------ the alphabet
        static Vector2[] L(params float[] xy)
        {
            var p = new Vector2[xy.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return p;
        }

        /// <summary>Part of an ellipse from angle a0 to a1 (degrees, 0 = right, counter-clockwise; a1 below a0 runs clockwise).</summary>
        static Vector2[] A(float cx, float cy, float rx, float ry, float a0, float a1)
        {
            int n = Mathf.Max(3, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 9f));
            var p = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad;
                p[i] = new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a));
            }
            return p;
        }
        static Vector2[] O(float cx, float cy, float rx, float ry) => A(cx, cy, rx, ry, 0, 360);
        static Vector2[] Dot(float x, float y) => new[] { new Vector2(x, y) };

        static Dictionary<char, Glyph> Define()
        {
            var g = new Dictionary<char, Glyph>();
            void Add(char c, float w, params Vector2[][] strokes)
            {
                var gl = new Glyph { w = w };
                gl.strokes.AddRange(strokes);
                g[c] = gl;
            }

            Add(' ', 0);

            // ---- lowercase: one round bowl (3.5 x 5) and plain stems; l and t end in a small curl
            Add('a', 3.5f, O(1.75f, 2.5f, 1.75f, 2.5f), L(3.5f, 5, 3.5f, 0));
            Add('b', 3.5f, L(0, 7.5f, 0, 0), O(1.75f, 2.5f, 1.75f, 2.5f));
            Add('c', 2.9f, A(1.75f, 2.5f, 1.75f, 2.5f, 50, 310));
            Add('d', 3.5f, O(1.75f, 2.5f, 1.75f, 2.5f), L(3.5f, 7.5f, 3.5f, 0));
            Add('e', 3.5f, L(0, 2.5f, 3.5f, 2.5f), A(1.75f, 2.5f, 1.75f, 2.5f, 0, 315));
            Add('f', 2.4f, L(1.0f, 0, 1.0f, 6.0f), A(2.2f, 6.0f, 1.2f, 1.5f, 180, 75), L(0, 5, 2.3f, 5));
            Add('g', 3.5f, O(1.75f, 2.5f, 1.75f, 2.5f), L(3.5f, 5, 3.5f, -0.6f), A(1.75f, -0.6f, 1.75f, 1.6f, 0, -150));
            Add('h', 3.3f, L(0, 7.5f, 0, 0), A(1.65f, 3.35f, 1.65f, 1.65f, 180, 0), L(3.3f, 3.35f, 3.3f, 0));
            Add('i', 0f, L(0, 5, 0, 0), Dot(0, 6.9f));
            Add('j', 1.5f, L(1.5f, 5, 1.5f, -0.9f), A(0.4f, -0.9f, 1.1f, 1.3f, 0, -110), Dot(1.5f, 6.9f));
            Add('k', 3.1f, L(0, 7.5f, 0, 0), L(3.0f, 5, 0, 2.2f), L(1.1f, 3.2f, 3.1f, 0));
            Add('l', 0.9f, L(0, 7.5f, 0, 1.0f), A(0.9f, 1.0f, 0.9f, 1.0f, 180, 270));
            Add('m', 5.4f, L(0, 5, 0, 0), A(1.35f, 3.65f, 1.35f, 1.35f, 180, 0), L(2.7f, 3.65f, 2.7f, 0), A(4.05f, 3.65f, 1.35f, 1.35f, 180, 0), L(5.4f, 3.65f, 5.4f, 0));
            Add('n', 3.3f, L(0, 5, 0, 0), A(1.65f, 3.35f, 1.65f, 1.65f, 180, 0), L(3.3f, 3.35f, 3.3f, 0));
            Add('o', 3.6f, O(1.8f, 2.5f, 1.8f, 2.5f));
            Add('p', 3.5f, L(0, 5, 0, -2.2f), O(1.75f, 2.5f, 1.75f, 2.5f));
            Add('q', 3.5f, O(1.75f, 2.5f, 1.75f, 2.5f), L(3.5f, 5, 3.5f, -2.2f));
            Add('r', 2.2f, L(0, 5, 0, 0), A(1.6f, 3.4f, 1.6f, 1.6f, 180, 60));
            Add('s', 2.9f, A(1.45f, 3.75f, 1.3f, 1.25f, 25, 270), A(1.45f, 1.25f, 1.4f, 1.25f, 90, -155));
            Add('t', 2.4f, L(0.9f, 6.6f, 0.9f, 1.1f), A(2.0f, 1.1f, 1.1f, 1.1f, 180, 290), L(0, 5, 2.3f, 5));
            Add('u', 3.3f, L(0, 5, 0, 1.65f), A(1.65f, 1.65f, 1.65f, 1.65f, 180, 360), L(3.3f, 5, 3.3f, 0));
            Add('v', 3.4f, L(0, 5, 1.7f, 0, 3.4f, 5));
            Add('w', 5.2f, L(0, 5, 1.3f, 0, 2.6f, 4.2f, 3.9f, 0, 5.2f, 5));
            Add('x', 3.2f, L(0, 5, 3.2f, 0), L(3.2f, 5, 0, 0));
            Add('y', 3.4f, L(0, 5, 1.87f, 0.4f), L(3.4f, 5, 1.0f, -2.2f));
            Add('z', 3.0f, L(0, 5, 3.0f, 5, 0, 0, 3.0f, 0));

            // ---- capitals
            Add('A', 4.6f, L(0, 0, 2.3f, 7, 4.6f, 0), L(0.95f, 2.3f, 3.65f, 2.3f));
            Add('B', 3.8f, L(0, 0, 0, 7, 1.9f, 7), A(1.9f, 5.3f, 1.6f, 1.7f, 90, -90), L(1.9f, 3.6f, 0, 3.6f), L(0, 3.6f, 2.0f, 3.6f), A(2.0f, 1.8f, 1.8f, 1.8f, 90, -90), L(2.0f, 0, 0, 0));
            Add('C', 4.45f, A(2.7f, 3.5f, 2.7f, 3.5f, 50, 310));
            Add('D', 4.3f, L(1.3f, 0, 0, 0, 0, 7, 1.3f, 7), A(1.3f, 3.5f, 3.0f, 3.5f, 90, -90));
            Add('E', 3.3f, L(3.3f, 7, 0, 7, 0, 0, 3.3f, 0), L(0, 3.6f, 2.8f, 3.6f));
            Add('F', 3.2f, L(3.2f, 7, 0, 7, 0, 0), L(0, 3.6f, 2.7f, 3.6f));
            Add('G', 5.2f, A(2.6f, 3.5f, 2.6f, 3.5f, 50, 355), L(5.19f, 3.2f, 3.0f, 3.2f));
            Add('H', 4.2f, L(0, 0, 0, 7), L(4.2f, 0, 4.2f, 7), L(0, 3.6f, 4.2f, 3.6f));
            Add('I', 1.6f, L(0, 7, 1.6f, 7), L(0.8f, 7, 0.8f, 0), L(0, 0, 1.6f, 0));      // with bars, so "AI" never reads as "Al"
            Add('J', 2.6f, L(2.6f, 7, 2.6f, 1.5f), A(1.3f, 1.5f, 1.3f, 1.5f, 0, -165));
            Add('K', 3.8f, L(0, 0, 0, 7), L(3.7f, 7, 0, 3.0f), L(1.35f, 4.4f, 3.8f, 0));
            Add('L', 3.0f, L(0, 7, 0, 0, 3.0f, 0));
            Add('M', 5.6f, L(0, 0, 0, 7, 2.8f, 1.6f, 5.6f, 7, 5.6f, 0));
            Add('N', 4.3f, L(0, 0, 0, 7, 4.3f, 0, 4.3f, 7));
            Add('O', 5.4f, O(2.7f, 3.5f, 2.7f, 3.5f));
            Add('P', 3.6f, L(0, 0, 0, 7, 1.8f, 7), A(1.8f, 5.15f, 1.8f, 1.85f, 90, -90), L(1.8f, 3.3f, 0, 3.3f));
            Add('Q', 5.4f, O(2.7f, 3.5f, 2.7f, 3.5f), L(3.2f, 1.6f, 5.3f, -0.5f));
            Add('R', 3.7f, L(0, 0, 0, 7, 1.8f, 7), A(1.8f, 5.15f, 1.8f, 1.85f, 90, -90), L(1.8f, 3.3f, 0, 3.3f), L(1.7f, 3.3f, 3.7f, 0));
            Add('S', 3.6f, A(1.8f, 5.25f, 1.65f, 1.75f, 25, 270), A(1.8f, 1.75f, 1.8f, 1.75f, 90, -155));
            Add('T', 4.0f, L(0, 7, 4.0f, 7), L(2.0f, 7, 2.0f, 0));
            Add('U', 4.2f, L(0, 7, 0, 2.1f), A(2.1f, 2.1f, 2.1f, 2.1f, 180, 360), L(4.2f, 2.1f, 4.2f, 7));
            Add('V', 4.4f, L(0, 7, 2.2f, 0, 4.4f, 7));
            Add('W', 6.4f, L(0, 7, 1.6f, 0, 3.2f, 5.6f, 4.8f, 0, 6.4f, 7));
            Add('X', 4.0f, L(0, 7, 4.0f, 0), L(4.0f, 7, 0, 0));
            Add('Y', 4.0f, L(0, 7, 2.0f, 3.2f, 4.0f, 7), L(2.0f, 3.2f, 2.0f, 0));
            Add('Z', 3.8f, L(0, 7, 3.8f, 7, 0, 0, 3.8f, 0));

            // ---- figures: all the same width, so a running timer does not jitter
            Add('0', 3.6f, O(1.8f, 3.5f, 1.8f, 3.5f));
            Add('1', 3.6f, L(1.2f, 5.6f, 2.4f, 7, 2.4f, 0));
            Add('2', 3.6f, A(1.8f, 5.2f, 1.7f, 1.8f, 160, -40), L(3.10f, 4.04f, 0.1f, 0, 3.5f, 0));
            Add('3', 3.6f, A(1.7f, 5.25f, 1.6f, 1.75f, 150, -90), A(1.7f, 1.75f, 1.8f, 1.75f, 90, -150));
            Add('4', 3.6f, L(2.8f, 0, 2.8f, 7, 0.1f, 2.2f, 3.6f, 2.2f));
            Add('5', 3.6f, L(3.2f, 7, 0.7f, 7, 0.5f, 4.0f), A(1.7f, 2.2f, 1.9f, 2.2f, 130, -150));
            Add('6', 3.6f, O(1.8f, 2.2f, 1.8f, 2.2f), A(3.0f, 2.2f, 3.0f, 4.8f, 90, 180));
            Add('7', 3.6f, L(0.1f, 7, 3.5f, 7, 1.3f, 0));
            Add('8', 3.6f, O(1.8f, 5.3f, 1.55f, 1.7f), O(1.8f, 1.8f, 1.8f, 1.8f));
            Add('9', 3.6f, O(1.8f, 4.8f, 1.8f, 2.2f), A(0.6f, 4.8f, 3.0f, 4.8f, -90, 0));

            // ---- punctuation and signs
            Add('.', 0f, Dot(0, 0.05f));
            Add(',', 0.4f, L(0.4f, 0.2f, 0.0f, -1.3f));
            Add(':', 0f, Dot(0, 0.3f), Dot(0, 4.3f));
            Add(';', 0.4f, Dot(0.4f, 4.3f), L(0.4f, 0.2f, 0.0f, -1.3f));
            Add('!', 0f, L(0, 7, 0, 2.2f), Dot(0, 0.05f));
            Add('?', 3.0f, A(1.5f, 5.4f, 1.5f, 1.6f, 165, -60), L(2.25f, 4.01f, 1.5f, 3.0f, 1.5f, 2.3f), Dot(1.5f, 0.05f));
            Add('\'', 0f, L(0, 7.3f, 0, 5.6f));
            Add('"', 1.3f, L(0, 7.3f, 0, 5.6f), L(1.3f, 7.3f, 1.3f, 5.6f));
            Add('`', 0.9f, L(0, 7.4f, 0.9f, 6.2f));
            Add('(', 1.5f, A(3.0f, 2.8f, 3.0f, 5.4f, 120, 240));
            Add(')', 1.5f, A(-1.5f, 2.8f, 3.0f, 5.4f, 60, -60));
            Add('[', 1.3f, L(1.3f, 7.6f, 0, 7.6f, 0, -1.9f, 1.3f, -1.9f));
            Add(']', 1.3f, L(0, 7.6f, 1.3f, 7.6f, 1.3f, -1.9f, 0, -1.9f));
            Add('{', 1.7f, A(1.7f, 6.7f, 0.8f, 0.9f, 90, 180), L(0.9f, 6.7f, 0.9f, 3.7f), A(0.1f, 3.7f, 0.8f, 0.85f, 0, -90), A(0.1f, 2.0f, 0.8f, 0.85f, 90, 0), L(0.9f, 2.0f, 0.9f, -1.0f), A(1.7f, -1.0f, 0.8f, 0.9f, 180, 270));
            Add('}', 1.7f, A(0.0f, 6.7f, 0.8f, 0.9f, 90, 0), L(0.8f, 6.7f, 0.8f, 3.7f), A(1.6f, 3.7f, 0.8f, 0.85f, 180, 270), A(1.6f, 2.0f, 0.8f, 0.85f, 90, 180), L(0.8f, 2.0f, 0.8f, -1.0f), A(0.0f, -1.0f, 0.8f, 0.9f, 0, -90));
            Add('-', 2.0f, L(0, 2.7f, 2.0f, 2.7f));
            Add('–', 3.0f, L(0, 2.7f, 3.0f, 2.7f));
            Add('—', 5.0f, L(0, 2.7f, 5.0f, 2.7f));
            Add('_', 3.6f, L(0, -1.0f, 3.6f, -1.0f));
            Add('+', 3.4f, L(0, 3.0f, 3.4f, 3.0f), L(1.7f, 1.3f, 1.7f, 4.7f));
            Add('=', 3.4f, L(0, 3.9f, 3.4f, 3.9f), L(0, 2.1f, 3.4f, 2.1f));
            Add('/', 2.6f, L(0, -1.0f, 2.6f, 7.6f));
            Add('\\', 2.6f, L(0, 7.6f, 2.6f, -1.0f));
            Add('|', 0f, L(0, 7.8f, 0, -2.0f));
            Add('*', 2.8f, L(1.4f, 7.0f, 1.4f, 4.2f), L(0.19f, 6.3f, 2.61f, 4.9f), L(0.19f, 4.9f, 2.61f, 6.3f));
            Add('#', 4.6f, L(1.0f, 0, 1.6f, 7), L(3.0f, 0, 3.6f, 7), L(0, 2.3f, 4.3f, 2.3f), L(0.3f, 4.7f, 4.6f, 4.7f));
            Add('%', 5.4f, O(1.1f, 5.6f, 1.1f, 1.4f), O(4.3f, 1.4f, 1.1f, 1.4f), L(4.4f, 7, 1.0f, 0));
            Add('&', 4.4f, L(4.4f, 0, 1.1f, 4.6f), A(2.0f, 5.6f, 1.25f, 1.4f, 215, -35), L(3.02f, 4.8f, 0.36f, 2.7f), A(2.0f, 1.8f, 1.9f, 1.8f, 150, 395));
            Add('@', 6.6f, A(3.3f, 2.6f, 3.3f, 3.6f, 15, 300), O(3.3f, 2.6f, 1.3f, 1.5f), L(4.6f, 4.1f, 4.6f, 1.9f), A(5.55f, 1.9f, 0.95f, 0.8f, 180, 360), L(6.5f, 1.9f, 6.49f, 3.53f));
            Add('$', 3.6f, A(1.8f, 5.25f, 1.65f, 1.75f, 25, 270), A(1.8f, 1.75f, 1.8f, 1.75f, 90, -155), L(1.8f, 7.8f, 1.8f, -0.8f));
            Add('^', 3.0f, L(0, 4.6f, 1.5f, 7.2f, 3.0f, 4.6f));
            Add('~', 3.6f, A(0.9f, 3.0f, 0.9f, 0.8f, 180, 0), A(2.7f, 3.0f, 0.9f, 0.8f, 180, 360));
            Add('<', 3.0f, L(3.0f, 5.2f, 0, 3.0f, 3.0f, 0.8f));
            Add('>', 3.0f, L(0, 5.2f, 3.0f, 3.0f, 0, 0.8f));
            Add('·', 0f, Dot(0, 2.8f));
            Add('•', 0.9f, O(0.45f, 2.8f, 0.45f, 0.45f), Dot(0.45f, 2.8f));
            Add('…', 3.8f, Dot(0, 0.05f), Dot(1.9f, 0.05f), Dot(3.8f, 0.05f));
            Add('‘', 0.4f, L(0, 7.3f, 0.4f, 5.7f));
            Add('’', 0.4f, L(0.4f, 7.3f, 0, 5.7f));
            Add('“', 1.7f, L(0, 7.3f, 0.4f, 5.7f), L(1.3f, 7.3f, 1.7f, 5.7f));
            Add('”', 1.7f, L(0.4f, 7.3f, 0, 5.7f), L(1.7f, 7.3f, 1.3f, 5.7f));
            Add('→', 4.6f, L(0, 2.8f, 4.6f, 2.8f), L(3.0f, 4.4f, 4.6f, 2.8f, 3.0f, 1.2f));
            Add('×', 3.2f, L(0.2f, 1.0f, 3.0f, 4.6f), L(0.2f, 4.6f, 3.0f, 1.0f));
            Add('°', 1.8f, O(0.9f, 6.1f, 0.9f, 0.9f));
            Add('✓', 4.6f, L(0, 2.6f, 1.6f, 0.6f, 4.6f, 5.6f));
            return g;
        }

        // ------------------------------------------------------------------ rasterising and saving
        static float DistSq(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a, ap = p - a;
            float l = ab.sqrMagnitude;
            float t = l > 1e-9f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / l) : 0f;
            return (ap - ab * t).sqrMagnitude;
        }

        static Font BuildWeight(string name, Dictionary<char, Glyph> glyphs, float radius, float gap)
        {
            var pixels = new byte[AtlasW * AtlasH];
            var infos = new List<CharacterInfo>();
            var keys = new List<char>(glyphs.Keys);
            keys.Sort();
            int penX = 2, penY = 2, rowH = 0;
            float edge = U * K;                       // atlas pixels per design unit

            foreach (char c in keys)
            {
                var gl = glyphs[c];
                // capitals and the hairline punctuation marks get a little more air than the round lowercase
                float air = gap + (char.IsUpper(c) ? 0.30f : (gl.w < 0.5f && !char.IsLetter(c) ? 0.45f : 0f));
                float bearing = air * 0.5f;
                int advance = Mathf.RoundToInt((c == ' ' ? SpaceAdvance : gl.w + air) * U);
                var ci = new CharacterInfo { index = c };
#pragma warning disable 618
                ci.width = advance; ci.flipped = false;
                if (gl.strokes.Count == 0) { ci.uv = new Rect(0, 0, 0, 0); ci.vert = new Rect(0, 0, 0, 0); infos.Add(ci); continue; }
#pragma warning restore 618

                // bounds of the ink in design units (dots are drawn a little fatter than the pen)
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var s in gl.strokes)
                {
                    float r = s.Length == 1 ? radius * 1.28f : radius;
                    foreach (var p in s) { minX = Mathf.Min(minX, p.x - r); maxX = Mathf.Max(maxX, p.x + r); minY = Mathf.Min(minY, p.y - r); maxY = Mathf.Max(maxY, p.y + r); }
                }
                // the quad, in font pixels relative to the pen position on the baseline
                int x0 = Mathf.FloorToInt((bearing + minX) * U) - 1, x1 = Mathf.CeilToInt((bearing + maxX) * U) + 1;
                int y0 = Mathf.FloorToInt(minY * U) - 1, y1 = Mathf.CeilToInt(maxY * U) + 1;
                int cw = (x1 - x0) * K, ch = (y1 - y0) * K;
                if (penX + cw + 2 > AtlasW) { penX = 2; penY += rowH + 2; rowH = 0; }
                if (penY + ch + 2 > AtlasH) throw new Exception("LPStory: atlas is full");
                rowH = Mathf.Max(rowH, ch);

                for (int j = 0; j < ch; j++)
                    for (int i = 0; i < cw; i++)
                    {
                        var p = new Vector2((x0 + (i + 0.5f) / K) / U - bearing, (y0 + (j + 0.5f) / K) / U);
                        float best = float.MaxValue;                 // signed distance to the nearest pen stroke, design units
                        foreach (var s in gl.strokes)
                        {
                            if (s.Length == 1) { best = Mathf.Min(best, (p - s[0]).magnitude - radius * 1.28f); continue; }
                            float d2 = float.MaxValue;
                            for (int k = 0; k < s.Length - 1; k++) d2 = Mathf.Min(d2, DistSq(p, s[k], s[k + 1]));
                            best = Mathf.Min(best, Mathf.Sqrt(d2) - radius);
                        }
                        float a = Mathf.Clamp01(0.5f - best * edge);
                        pixels[(penY + j) * AtlasW + penX + i] = (byte)Mathf.RoundToInt(a * 255f);
                    }

#pragma warning disable 618
                ci.uv = new Rect(penX / (float)AtlasW, penY / (float)AtlasH, cw / (float)AtlasW, ch / (float)AtlasH);
                ci.vert = new Rect(x0, y1, x1 - x0, -(y1 - y0));
#pragma warning restore 618
                infos.Add(ci);
                penX += cw + 2;
            }

            // atlas texture (kept readable so a re-run can repaint the same asset)
            string texPath = $"{FontDir}/{name}_Atlas.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null || tex.width != AtlasW || tex.height != AtlasH || tex.format != TextureFormat.Alpha8 || !tex.isReadable)
            {
                if (tex != null) AssetDatabase.DeleteAsset(texPath);
                tex = new Texture2D(AtlasW, AtlasH, TextureFormat.Alpha8, true, true) { name = name + "_Atlas" };
                AssetDatabase.CreateAsset(tex, texPath);
            }
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = 4; tex.mipMapBias = -0.4f;
            tex.LoadRawTextureData(PadForMips(pixels, tex));
            tex.Apply(true, false);
            EditorUtility.SetDirty(tex);

            string matPath = $"{FontDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("GUI/Text Shader")) { name = name }; AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = Shader.Find("GUI/Text Shader");
            mat.mainTexture = tex; mat.color = Color.white;
            EditorUtility.SetDirty(mat);

            string fontPath = $"{FontDir}/{name}.fontsettings";
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) { font = new Font { name = name }; AssetDatabase.CreateAsset(font, fontPath); }
            font.material = mat;
            font.characterInfo = infos.ToArray();
            var so = new SerializedObject(font);
            SetFloat(so, "m_LineSpacing", LineSpacing);
            SetFloat(so, "m_FontSize", 64f);
            SetFloat(so, "m_Ascent", 7.5f * U);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(font);
            return font;
        }

        static void SetFloat(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
            else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(v);
        }

        /// <summary>LoadRawTextureData wants the whole mip chain; only level 0 is ours, Apply(true) recomputes the rest.</summary>
        static byte[] PadForMips(byte[] level0, Texture2D tex)
        {
            int total = tex.GetRawTextureData().Length;
            if (total == level0.Length) return level0;
            var all = new byte[total];
            Buffer.BlockCopy(level0, 0, all, 0, level0.Length);
            return all;
        }
    }
}
