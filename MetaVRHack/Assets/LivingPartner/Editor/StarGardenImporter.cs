using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.MR;
using LivingPartner.Shared;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    [ScriptedImporter(1, "starbed")]
    public sealed class StarGardenImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var art = ScriptableObject.CreateInstance<StarGardenMeshes>();
            art.stages = new Mesh[(int)RadishStage.Ripe + 1];
            for (int i = 0; i < art.stages.Length; i++)
            {
                var stage = (RadishStage)i;
                var mesh = GardenMesh(stage);
                mesh.name = stage.ToString();
                art.stages[i] = mesh;
                ctx.AddObjectToAsset(mesh.name, mesh);
            }
            art.fruit = StarFruitMesh();
            ctx.AddObjectToAsset("Fruit", art.fruit);
            ctx.AddObjectToAsset("StarGarden", art);
            ctx.SetMainObject(art);
        }

        static Mesh StarFruitMesh()
        {
            var v = new Vector3[12]; var c = new Color[12]; var tris = new int[60];
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI * (.5f + i / 5f), r = i % 2 == 0 ? .25f : .12f;
                v[i] = new Vector3(Mathf.Cos(angle)*r, Mathf.Sin(angle)*r, 0);
                c[i] = i % 2 == 0 ? new Color(1f,.86f,.35f) : new Color(.90f,.47f,.10f);
                int t = i * 6, next = (i + 1) % 10;
                tris[t] = 10; tris[t+1] = next; tris[t+2] = i;
                tris[t+3] = 11; tris[t+4] = i; tris[t+5] = next;
            }
            v[10] = Vector3.back * .095f; v[11] = Vector3.forward * .095f;
            c[10] = new Color(1f,.97f,.68f); c[11] = new Color(1f,.77f,.25f);
            var mesh = new Mesh { name = "Faceted star fruit", vertices = v, triangles = tris, colors = c };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static Mesh GardenMesh(RadishStage stage)
        {
            var parts = new List<ShapePart>();
            void Part(string shape, string color, float x, float y, float z, float w, float h, float d, float tilt = 0, float yaw = 0)
            {
                parts.Add(new ShapePart { shape = shape, color = color, x = x, y = y, z = z,
                    sx = w, sy = h, sz = d, rz = tilt, ry = yaw, round = 0.35f });
            }
            Part("cylinder", "#A95C43", 0, .17f, 0, .83f, .34f, .83f);
            Part("cylinder", "#DA8866", 0, .32f, 0, 1, .15f, 1);
            Part("cylinder", stage >= RadishStage.Sprout ? "#55392F" : "#79503B", 0, .4f, 0, .85f, .045f, .85f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 2.4f, r = .19f + .032f * (i % 5);
                Part("sphere", "#9A7254", Mathf.Cos(a) * r, .422f, Mathf.Sin(a) * r, .032f, .018f, .022f);
            }
            if (stage == RadishStage.Seed)
                Part("sphere", "#E9BD73", 0, .445f, 0, .14f, .075f, .10f, -16);
            if (stage >= RadishStage.Sprout)
            {
                bool ripe = stage == RadishStage.Ripe;
                if (ripe) Part("cylinder", "#537950", 0, .60f, 0, .06f, .34f, .06f);
                float scale = stage == RadishStage.Sprout ? .53f : 1f;
                float baseY = ripe ? .56f : .42f;
                int leaves = stage == RadishStage.Sprout ? 2 : 5;
                for (int i = 0; i < leaves; i++)
                {
                    float angle = i * 360f / leaves, rad = angle * Mathf.Deg2Rad;
                    Part("sphere", i % 2 == 0 ? "#54845B" : "#8CB55B",
                        .10f * scale * Mathf.Cos(rad), baseY + .13f * scale, -.10f * scale * Mathf.Sin(rad),
                        .13f * scale, .35f * scale, .065f * scale, -36, angle);
                }
            }
            return PropMeshes.BuildParts(parts, out _);
        }
    }
}
