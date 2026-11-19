using System.Net.Http.Headers;

namespace AniKatha.Core;

public sealed partial class AddonClient
{
    internal async Task<DownloadedAsset> DownloadMediaAsync(string url, string folder, int index, bool video,
        Func<long, Task> report, CancellationToken token)
    {
        if (!AddonResources.MediaUrl(url)) throw new InvalidDataException("Invalid media URL.");
        var uri = new Uri(url);
        var addresses = await resolve(uri.Host, token);
        if (addresses.Length == 0 || addresses.Any(IsPrivate))
            throw new InvalidDataException("Media must use a public address.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(video ? "video/mp4" : "image/*"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new InvalidDataException("The provider redirected this download.");
        response.EnsureSuccessStatusCode();
        var expectedBytes = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);

        var buffer = new byte[65536];
        var read = await ReadWithDeadlineAsync(input, buffer.AsMemory(0, 16), true, token);
        var extension = MediaExtension(buffer.AsSpan(0, read), video);
        var name = video ? "video.mp4" : $"page-{index:D4}.{extension}";
        await using var output = new FileStream(Path.Combine(folder, name), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, buffer.Length, FileOptions.Asynchronous);
        long bytes = 0;
        while (read > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            bytes += read;
            await report(bytes);
            read = await ReadWithDeadlineAsync(input, buffer, false, token);
        }
        if (expectedBytes.HasValue && bytes != expectedBytes.Value)
            throw new InvalidDataException("The provider sent an incomplete asset.");
        return new(name, bytes);
    }

    private static async Task<int> ReadWithDeadlineAsync(Stream stream, Memory<byte> buffer, bool readHeader, CancellationToken token)
    {
        using var read = CancellationTokenSource.CreateLinkedTokenSource(token);
        read.CancelAfter(TimeSpan.FromSeconds(30));
        if (readHeader) return await stream.ReadAtLeastAsync(buffer, buffer.Length, false, read.Token);
        return await stream.ReadAsync(buffer, read.Token);
    }

    private static string MediaExtension(ReadOnlySpan<byte> header, bool video)
    {
        if (video)
        {
            if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8)) return "mp4";
            throw new InvalidDataException("This source did not return an MP4 file.");
        }
        if (header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "jpg";
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8)) return "gif";
        if (header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return "webp";
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8) &&
            (header[8..12].SequenceEqual("avif"u8) || header[8..12].SequenceEqual("avis"u8))) return "avif";
        throw new InvalidDataException("A manga page was not a supported image.");
    }
}
