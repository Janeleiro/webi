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
            state.Notes.Add(new StickyNote { Text = "café" });
            state.Save();
            state.Affinity = 43;
            state.Save(); // overwrites the existing file

            Assert.False(File.Exists(path + ".tmp"));
            var again = AppState.Load(path);
            Assert.Equal(43, again.Affinity);
            Assert.Equal("café", Assert.Single(again.Notes).Text);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
