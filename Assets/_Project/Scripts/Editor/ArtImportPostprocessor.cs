using UnityEditor;
using UnityEngine;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Gives textures dropped into Assets/_Project/Art/ the import settings the UI expects,
    /// so nobody has to click through the inspector for every file.
    /// Only runs the first time a file is imported (no .meta yet) — manual tweaks made later
    /// in the inspector are kept.
    ///  - Art/Sprites/**  (antiques, contracts, slot backdrop): Sprite, Full Rect, mipmaps on
    ///    (the 51x68 inventory thumbnail is a big downscale), max 1024, high-quality compression.
    ///  - Art/UI/**       (icons, frames): Sprite, Full Rect, no mipmaps, max 512.
    /// </summary>
    public class ArtImportPostprocessor : AssetPostprocessor
    {
        private const string ArtRoot = "Assets/_Project/Art/";
        private const string SpritesRoot = ArtRoot + "Sprites/";
        private const string UiRoot = ArtRoot + "UI/";

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(ArtRoot))
                return;

            var importer = (TextureImporter)assetImporter;
            if (!importer.importSettingsMissing)
                return;

            bool isSprite = path.StartsWith(SpritesRoot);
            bool isUi = path.StartsWith(UiRoot);
            if (!isSprite && !isUi)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = isSprite;
            importer.maxTextureSize = isSprite ? 1024 : 512;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            // Full Rect: UI Images with Preserve Aspect / Simple type behave predictably
            // (Tight meshes can clip soft edges of cut-out objects).
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }
    }
}
