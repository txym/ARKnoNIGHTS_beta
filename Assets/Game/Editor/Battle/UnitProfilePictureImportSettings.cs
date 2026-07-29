using System;
using UnityEditor;

internal sealed class UnitProfilePictureImportSettings : AssetPostprocessor
{
    private const string ProfilePicturePrefix =
        "Assets/Resources/ProfilePicture/UIImage_";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(
                ProfilePicturePrefix,
                StringComparison.Ordinal)
            || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
    }
}
