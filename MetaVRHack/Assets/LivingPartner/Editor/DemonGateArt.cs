using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LivingPartner.MR;
using UnityEditor;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    public static class DemonGateArt
    {
        // TV WebGL and the headset must draw the same character as it crosses the glass.
        [MenuItem("LivingPartner/Export Demon Gate meshes for Mac TV")]
        public static void ExportMeshes()
        {
            SwarmMeshes.Build(out var models, out var flashes);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Companion/LivingPartner.Mac/wwwroot/art/swarm-meshes.json"));
            string[] keys = { "Hopper", "Flyer", "Tank", "Splitter", "Mini", "Boss" };
            var text = new StringBuilder(1500000);
            text.Append("{\"meshes\":{");
            try
            {
                for (int i=0;i<models.Length;i++)
                {
                    if (i>0) text.Append(',');
                    var model=models[i];
                    text.Append('"').Append(keys[i]).Append("\":{\"vertices\":[");
                    Vectors(text,model.vertices);
                    text.Append("],\"indices\":[").Append(string.Join(",",model.triangles)).Append("],\"normals\":[");
                    Vectors(text,model.normals);
                    text.Append("],\"colors\":[");
                    var colors=model.colors;
                    for(int j=0;j<colors.Length;j++)
                    {
                        if(j>0) text.Append(',');
                        var c=colors[j]; Number(text,c.r); text.Append(','); Number(text,c.g);
                        text.Append(','); Number(text,c.b); text.Append(','); Number(text,c.a);
                    }
                    text.Append("]}");
                }
                text.Append("}}");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,text.ToString(),new UTF8Encoding(false));
            }
            finally
            {
                foreach (var mesh in models) UnityEngine.Object.DestroyImmediate(mesh);
                foreach (var mesh in flashes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        static void Vectors(StringBuilder text,Vector3[] vectors)
        {
            for(int i=0;i<vectors.Length;i++)
            {
                if(i>0) text.Append(',');
                var v=vectors[i]; Number(text,v.x); text.Append(','); Number(text,v.y); text.Append(','); Number(text,v.z);
            }
        }

        static void Number(StringBuilder text,float value) => text.Append(value.ToString("0.####",CultureInfo.InvariantCulture));
    }
}
