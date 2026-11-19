using AniKatha.Core;
using AniKatha.UI.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;

internal static class ReaderChecks
{
    public static async Task RunAsync(BunitContext context, LocalStore store)
    {
        var key = Guid.NewGuid().ToString();
        var pages = new[] { "https://images.example/p1.webp", "https://images.example/p2.webp", "https://images.example/p3.webp" };
        var reader = context.Render<ReadMangaPreview>(p => p.Add(x => x.ChapterKey, key).Add(x => x.TitleSettingsKey, key).Add(x => x.Pages, pages));
        async Task Change(string label, string value) => await reader.InvokeAsync(() =>
            reader.FindAll("label").Single(x => x.TextContent.TrimStart().StartsWith(label, StringComparison.Ordinal))
                .QuerySelector("select")!.ChangeAsync(new ChangeEventArgs { Value = value }));
        async Task Jump(string value) => await reader.InvokeAsync(async () =>
        {
            await reader.Find(".reader-jump input").ChangeAsync(new ChangeEventArgs { Value = value });
            await reader.Find(".reader-jump").SubmitAsync();
        });
        async Task Click(string label) => await reader.InvokeAsync(() =>
            reader.FindAll("button").Single(x => x.TextContent.Trim() == label).ClickAsync(new()));
        void Status(string value) => reader.WaitForAssertion(() =>
        {
            if (!reader.Markup.Contains(value)) throw new InvalidOperationException(value);
        });

        Status("Page 1 of 3");
        await Jump("4");
        Status("Enter a page from 1 to 3");
        await Jump("2");
        Status("Page 2 of 3");
        await Change("Reading mode", "rtl");
        Status("← Next page");
        await Change("Page fit", "page");
        await Change("Zoom", "150");
        await Click("Remember layout for this title");
        if (await store.GetReadingPageAsync(key) != 2) throw new InvalidOperationException("Layout changed reading progress");
        reader.Dispose();
        reader = context.Render<ReadMangaPreview>(p => p.Add(x => x.ChapterKey, key).Add(x => x.TitleSettingsKey, key).Add(x => x.Pages, pages));
        Status("Page 2 of 3");
        Status("reader-rtl");
        Status("--reader-zoom:150%");
        await new ReaderPreferences("vertical", "width").SaveAsync(store, ReaderPreferences.GlobalKey);
        await Click("Use default layout");
        Status("reader-stack");
        Status("Page 2 of 3");
        await reader.InvokeAsync(() => reader.Instance.ObservePage(3));
        Status("Page 3 of 3");
        if (await store.GetReadingPageAsync(key) != 3) throw new InvalidOperationException("Observed page not saved");
        Console.WriteLine("PASS: reader jump, right-to-left navigation, zoom, legacy page restore, title override and vertical page tracking.");
    }
}
