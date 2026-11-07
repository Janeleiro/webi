using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Webi.Tests;

public class ChangelogTests
{
    /// <summary>application/config/version, read from project.godot (found walking up from the test's folder).</summary>
    static string ProjectVersion()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "project.godot");
            if (File.Exists(file))
                return Regex.Match(File.ReadAllText(file), "^config/version=\"([^\"]+)\"", RegexOptions.Multiline).Groups[1].Value;
        }
        throw new FileNotFoundException("project.godot");
    }

    [Fact]
    public void The_newest_release_is_the_project_version() =>
        Assert.Equal(ProjectVersion(), Changelog.Releases[0].Version);

    [Fact]
    public void Releases_go_newest_first_each_with_changes_and_a_date()
    {
        var versions = Changelog.Releases.Select(r => Version.Parse(r.Version)).ToList();
        Assert.Equal(versions.OrderByDescending(v => v), versions);
        Assert.Equal(versions.Count, versions.Distinct().Count());
        foreach (var r in Changelog.Releases)
        {
            Assert.NotEmpty(r.Changes);
            Assert.True(DateTime.TryParseExact(r.Date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _), r.Date);
        }
    }
}
