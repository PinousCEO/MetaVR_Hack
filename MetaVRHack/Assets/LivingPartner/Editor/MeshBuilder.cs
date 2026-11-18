using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// Authoring-time mesh assets that are made in code. The partner's body is not one of them: it comes from the
    /// character model (PartnerModelBuilder). Nothing here runs in a player.
    /// </summary>
    public static class MeshBuilder
    {
        /// <summary>A unit quad in the XY plane, facing -Z: every motion-graphics quad and the partner's cut face use it.</summary>
        public static Mesh Quad(string name)
        {
            var quad = new Mesh { name = name };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return quad;
        }

        /// <summary>A sphere of diameter 1 around the origin (the ball of the tennis game; its scale is its diameter).</summary>
        public static Mesh Sphere(string name, int around = 28, int rings = 18)
        {
            var verts = new System.Collections.Generic.List<Vector3>(); var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>(); var tris = new System.Collections.Generic.List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings, lat = (v - 0.5f) * Mathf.PI;
                for (int a = 0; a <= around; a++)
                {
                    float u = a / (float)around, lon = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    verts.Add(n * 0.5f); normals.Add(n); uvs.Add(new Vector2(u, v));
                }
            }
            int row = around + 1;
            for (int r = 0; r < rings; r++)
                for (int a = 0; a < around; a++)
                {
                    int i0 = r * row + a, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetNormals(normals); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
