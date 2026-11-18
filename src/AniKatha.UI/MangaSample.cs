namespace AniKatha.UI;

internal static class MangaSample
{
 internal const string Title = "The Midnight Lighthouse";
 internal const string ChapterKey = "midnight-lighthouse:chapter-1";

 internal static IReadOnlyList<string> PageDescriptions { get; } =
 [
  "A small cat robot approaches a lighthouse. The beacon goes dark, and the robot opens the hatch.",
  "The robot climbs the staircase, finds the fallen star-shaped fuse, and fits it into the lamp.",
  "The beacon lights up and guides a fishing boat past the rocks. The robot watches from the balcony."
 ];
}
