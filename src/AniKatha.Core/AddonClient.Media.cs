using System.Buffers.Binary;
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
            FileAccess.ReadWrite, FileShare.None, buffer.Length, FileOptions.Asynchronous);
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
        await output.FlushAsync(token);
        if (!HasCompleteContainer(output, extension))
            throw new InvalidDataException("The provider sent a truncated or unreadable file.");
        return new(name, bytes);
    }

    private static async Task<int> ReadWithDeadlineAsync(Stream stream, Memory<byte> buffer, bool readHeader, CancellationToken token)
    {
        using var read = CancellationTokenSource.CreateLinkedTokenSource(token);
        read.CancelAfter(TimeSpan.FromSeconds(30));
        if (readHeader) return await stream.ReadAtLeastAsync(buffer, buffer.Length, false, read.Token);
        return await stream.ReadAsync(buffer, read.Token);
    }

    private static bool HasCompleteContainer(Stream file, string extension)
    {
        if (extension is "mp4" or "avif") return HasIsoBoxes(file, extension == "mp4");
        if (extension == "webp")
        {
            Span<byte> header = stackalloc byte[8];
            file.Position = 0;
            file.ReadExactly(header);
            return BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) + 8L == file.Length;
        }

        ReadOnlySpan<byte> trailer = extension switch
        {
            "png" => [0, 0, 0, 0, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82],
            "jpg" => [0xff, 0xd9],
            "gif" => [0x3b],
            _ => []
        };
        if (trailer.IsEmpty || file.Length <= trailer.Length) return false;
        Span<byte> actual = stackalloc byte[trailer.Length];
        file.Position = file.Length - trailer.Length;
        file.ReadExactly(actual);
        return actual.SequenceEqual(trailer);
    }

    private static bool HasIsoBoxes(Stream file, bool video)
    {
        var offset = 0L;
        var metadata = false;
        var payload = false;
        Span<byte> header = stackalloc byte[8];
        Span<byte> extendedSize = stackalloc byte[8];
        while (offset < file.Length)
        {
            if (file.Length - offset < header.Length) return false;
            file.Position = offset;
            file.ReadExactly(header);
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(header);
            var minimum = 8L;
            if (size == 0) size = file.Length - offset;
            if (size == 1)
            {
                if (file.Length - offset < 16) return false;
                file.ReadExactly(extendedSize);
                size = BinaryPrimitives.ReadInt64BigEndian(extendedSize);
                minimum = 16;
            }
            if (size < minimum || size > file.Length - offset) return false;
            var type = header[4..];
            metadata |= video ? type.SequenceEqual("moov"u8) : type.SequenceEqual("meta"u8);
            payload |= size > minimum && type.SequenceEqual("mdat"u8);
            offset += size;
        }
        return metadata && (!video || payload);
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
