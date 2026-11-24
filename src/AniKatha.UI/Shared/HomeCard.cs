using AniKatha.Core;

namespace AniKatha.UI.Shared;

public sealed record HomeCard(string Title, string? Poster, bool Manga = false, string? Caption = null, string? Note = null,
    string? Link = null, double? Progress = null, int? Rank = null, Media? Detail = null, HomeProviderTitle? Provider = null, string? Previous = null);

public sealed record HomeProviderTitle(AddonResources Source, string Type, SavedTitle Title, string? Unit = null, int? AniListId = null);
