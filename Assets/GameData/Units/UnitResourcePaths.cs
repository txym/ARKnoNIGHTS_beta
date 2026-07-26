using System;
using System.Globalization;

public static class UnitResourcePaths
{
    private const string CharactersRoot = "Characters";
    private const string ProfilePictureRoot = "ProfilePicture";
    private const string ProfilePicturePrefix = "UIImage_";

    public static string BuildCharacterFolderName(int typeId, string resourceKey)
    {
        if (typeId <= 0) throw new ArgumentOutOfRangeException(nameof(typeId));

        var normalizedResourceKey = NormalizeSegment(resourceKey, nameof(resourceKey));
        return typeId.ToString(CultureInfo.InvariantCulture) + "_" + normalizedResourceKey;
    }

    public static string BuildSkeletonDataResourcePath(int typeId, string resourceKey, string skeletonDataResourceName)
    {
        var normalizedSkeletonName = NormalizeSegment(skeletonDataResourceName, nameof(skeletonDataResourceName));
        return CharactersRoot + "/" + BuildCharacterFolderName(typeId, resourceKey) + "/" + normalizedSkeletonName;
    }

    public static string BuildProfilePictureResourceName(int typeId, string resourceKey)
    {
        return ProfilePicturePrefix + BuildCharacterFolderName(typeId, resourceKey);
    }

    public static string BuildProfilePictureResourcePath(int typeId, string resourceKey)
    {
        return ProfilePictureRoot + "/" + BuildProfilePictureResourceName(typeId, resourceKey);
    }

    private static string NormalizeSegment(string value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("A non-empty resource segment is required.", parameterName);
        if (normalized.IndexOf('/') >= 0) throw new ArgumentException("A resource segment cannot contain a path separator.", parameterName);
        return normalized;
    }
}
