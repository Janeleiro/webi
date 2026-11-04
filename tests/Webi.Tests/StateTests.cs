using System;
using System.IO;
using Webi;
using Xunit;

namespace Webi.Tests;

public class StateTests
{
    [Fact]
    public void Saves_and_loads_without_leaving_a_temp_file()
    {
        var dir = Directory.CreateTempSubdirectory("webi");
        try
        {
            var path = Path.Combine(dir.FullName, "state.json");
            var state = AppState.Load(path);
            state.Affinity = 42;
            state.Notes.Add(new StickyNote { Text = "café", X = 120.5f, Y = 300 });
            state.Save();
            state.Affinity = 43;
            state.Save(); // overwrites the existing file

            Assert.False(File.Exists(path + ".tmp"));
            var again = AppState.Load(path);
            Assert.Equal(43, again.Affinity);
            var note = Assert.Single(again.Notes);
            Assert.Equal("café", note.Text);
            Assert.Equal(120.5f, note.X);
            Assert.Equal(300f, note.Y);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Notes_saved_before_positions_load_without_one()
    {
        var dir = Directory.CreateTempSubdirectory("webi");
        try
        {
            var path = Path.Combine(dir.FullName, "state.json");
            File.WriteAllText(path, """{ "notes": [ { "text": "café", "created": "2026-10-01T10:00:00" } ] }""");
            var note = Assert.Single(AppState.Load(path).Notes);
            Assert.Equal("café", note.Text);
            Assert.Null(note.X);
            Assert.Null(note.Y);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Old_note_positions_move_to_desktop_coordinates_once()
    {
        var state = new AppState();
        state.Notes.Add(new StickyNote { Text = "café", X = 2100, Y = 300 });
        state.Notes.Add(new StickyNote { Text = "sem lugar" });

        state.MoveNotesToDesktop(-1920, 0);
        state.MoveNotesToDesktop(-1920, 0); // already converted: no-op

        Assert.True(state.NotesOnDesktop);
        Assert.True(state.Dirty);
        Assert.Equal(180f, state.Notes[0].X);
        Assert.Equal(300f, state.Notes[0].Y);
        Assert.Null(state.Notes[1].X);
    }

    [Fact]
    public void Converted_notes_stay_converted_after_reload()
    {
        var dir = Directory.CreateTempSubdirectory("webi");
        try
        {
            var path = Path.Combine(dir.FullName, "state.json");
            var state = AppState.Load(path);
            state.Notes.Add(new StickyNote { Text = "café", X = 100, Y = 300 });
            state.MoveNotesToDesktop(-1920, 0);
            state.Save();

            var again = AppState.Load(path);
            again.MoveNotesToDesktop(-1920, 0);
            Assert.Equal(-1820f, again.Notes[0].X);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
