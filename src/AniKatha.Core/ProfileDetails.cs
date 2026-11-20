namespace AniKatha.Core;

public sealed record ProfileDetails(string Username = "", string Bio = "", string Website = "")
{
    public const int AvatarLimit = 1024 * 1024;

    public string? Validate()
    {
        if (Username.Length != 0 && (Username.Length is < 3 or > 10 || !Username.All(char.IsAsciiLetterOrDigit)))
            return "Use 3–10 letters or digits for the username.";
        if (Bio.Length > 500)
            return "Keep your bio to 500 characters or fewer.";
        if (Website.Length != 0 && (Website.Length > 500 || !Uri.TryCreate(Website, UriKind.Absolute, out var url) ||
            url.Scheme is not ("https" or "http") || string.IsNullOrEmpty(url.Host) || url.UserInfo.Length != 0))
            return "Enter a valid HTTP or HTTPS website.";
        return null;
    }

    public static string CreateAvatar(byte[] bytes)
    {
        if (bytes.Length > AvatarLimit) throw new InvalidOperationException("Choose an image under 1 MB.");
        ReadOnlySpan<byte> image = bytes;
        var format = image.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "png" :
            image.StartsWith(new byte[] { 255, 216, 255 }) ? "jpeg" :
            image.Length >= 12 && image[..4].SequenceEqual("RIFF"u8) && image[8..12].SequenceEqual("WEBP"u8) ? "webp" : null;
        if (format is null) throw new InvalidOperationException("Choose a PNG, JPEG, or WebP image.");
        return $"data:image/{format};base64,{Convert.ToBase64String(bytes)}";
    }

    internal static bool IsAvatar(string value)
    {
        var comma = value.IndexOf(',');
        if (!value.StartsWith("data:image/", StringComparison.Ordinal) || comma < 0 || value.Length > AvatarLimit * 4 / 3 + 50) return false;
        try { return CreateAvatar(Convert.FromBase64String(value[(comma + 1)..])) == value; }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException) { return false; }
    }
}
