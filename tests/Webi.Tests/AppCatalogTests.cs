using Webi.Assistant;
using Xunit;

namespace Webi.Tests;

public class AppCatalogTests
{
    [Theory]
    [InlineData("Spotify", "Spotify Premium", "spotify")]
    [InlineData("Discord", "#geral - Discord", "discord")]
    [InlineData("Godot_v4.7.2-stable_mono_win64", "Webi - Godot Engine", "godot")]
    public void Desktop_apps_match_by_process(string process, string title, string id) =>
        Assert.Equal(id, AppCatalog.Identify(process, title)?.Id);

    [Fact]
    public void Browsers_match_by_title() =>
        Assert.Equal("youtube", AppCatalog.Identify("chrome", "Gatinhos - YouTube - Google Chrome")?.Id);

    [Fact]
    public void Longest_title_hint_wins() =>
        Assert.Equal("music", AppCatalog.Identify("msedge", "Lo-fi - YouTube Music - Microsoft Edge")?.Id);

    [Fact]
    public void Unknown_apps_are_not_identified() =>
        Assert.Null(AppCatalog.Identify("notepad-desconhecido", "YouTube"));

    [Fact]
    public void Meetings_are_quiet()
    {
        var zoom = AppCatalog.Identify("Zoom", "Reunião Zoom");
        Assert.NotNull(zoom);
        Assert.True(AppCatalog.IsQuiet(zoom!.Group));
    }
}
