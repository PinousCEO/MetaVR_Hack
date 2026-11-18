using UnityEditor;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>How the interface art (Art/Ui, written by tools/ui-art/build_ui_art.py) is imported: pictures with see-through parts, sharp at any distance.</summary>
    public sealed class UiArtImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (assetPath.IndexOf("/LivingPartner/Art/Ui/", System.StringComparison.Ordinal) < 0) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true; ti.streamingMipmaps = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4;
            ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
