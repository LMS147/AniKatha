using AniKatha.Core;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace AniKatha.Mobile;

public sealed class OfflineMediaWebView : BlazorWebView
{
    internal LocalProfiles? Profiles { get; set; }

    public override IFileProvider CreateFileProvider(string contentRootDir) =>
        new MediaFileProvider(base.CreateFileProvider(contentRootDir), Profiles);

    private sealed class MediaFileProvider(IFileProvider appFiles, LocalProfiles? profiles) : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath)
        {
            var relative = subpath.TrimStart('/');
            if (!relative.StartsWith("offline-media/", StringComparison.Ordinal)) return appFiles.GetFileInfo(subpath);
            var parts = relative.Split('/');
            if (parts.Length != 4 || profiles?.Session is not { } session || session.Id != parts[1])
                return new NotFoundFileInfo(relative);
            var path = session.Downloads.FindAsset(parts[2], parts[3]);
            return path is null ? new NotFoundFileInfo(relative) : new MediaFile(new FileInfo(path));
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => appFiles.GetDirectoryContents(subpath);
        public IChangeToken Watch(string filter) => appFiles.Watch(filter);
    }

    private sealed class MediaFile(FileInfo file) : IFileInfo
    {
        public bool Exists => file.Exists;
        public long Length => file.Length;
        public string PhysicalPath => file.FullName;
        public string Name => file.Name;
        public DateTimeOffset LastModified => new(file.LastWriteTimeUtc);
        public bool IsDirectory => false;
        public Stream CreateReadStream() => file.Open(FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    }
}
