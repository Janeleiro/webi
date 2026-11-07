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

    [Theory]
    [InlineData("mstsc", "10.10.60.58 - Conexão de Área de Trabalho Remota")]
    [InlineData("mstsc", "youtube-srv - Remote Desktop Connection")] // the host name is not an app
    [InlineData("msrdc", "Desktop da empresa")]
    [InlineData("msrdc", "TEAMS-HOST01")]       // a workspace named like an app is still the desktop
    [InlineData("msrdc", "Workspace Office")]
    public void Whole_remote_desktops_are_the_remote_app(string process, string title)
    {
        var app = AppCatalog.Identify(process, title);
        Assert.Equal("remote", app?.Id);
        Assert.Equal(AppGroup.Remote, app!.Group);
        Assert.NotEmpty(AppCatalog.GroupLines[AppGroup.Remote]);
    }

    [Theory]
    [InlineData("Gatinhos - YouTube - Google Chrome (Remoto)", "youtube")]
    [InlineData("Livro1 - Excel (Remoto)", "excel")]
    [InlineData("Documento1 - Word (Remote)", "word")]
    [InlineData("Trocar Password (Remoto)", "remote")] // "Word" only as a whole word
    [InlineData("Planilha Teams Q3 - Excel (Remoto)", "excel")]   // the app is the last part, not the document's name
    [InlineData("Notas Meet - Word (Remoto)", "word")]            // not Google Meet (quiet mode)
    [InlineData("Terminal de vendas - Excel (Remoto)", "excel")]
    [InlineData("Spotify Premium (Remoto)", "spotify")]
    [InlineData("Gatinhos - YouTube - Microsoft\u200b Edge (Remoto)", "youtube")]
    public void RemoteApps_are_identified_by_their_title(string title, string id) =>
        Assert.Equal(id, AppCatalog.Identify("mstsc", title)?.Id);
}
