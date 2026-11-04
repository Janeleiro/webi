using System;
using System.Collections.Generic;
using System.Linq;
using Webi.Assistant;
using Xunit;

namespace Webi.Tests;

public class RecentPickerTests
{
    [Fact]
    public void Never_repeats_within_its_memory()
    {
        var lines = Enumerable.Range(0, 6).Select(i => $"fala {i}").ToArray();
        var picker = new RecentPicker(5, new Random(7));
        var picked = new List<string>();
        for (int i = 0; i < 300; i++) picked.Add(picker.Pick(lines));
        // With 6 lines and 5 remembered, each pick differs from the previous 5.
        for (int i = 5; i < picked.Count; i++)
            Assert.DoesNotContain(picked[i], picked.Skip(i - 5).Take(5));
    }

    [Fact]
    public void Repeats_when_every_item_was_said_recently()
    {
        var picker = new RecentPicker(10, new Random(1));
        Assert.Equal("única", picker.Pick(new[] { "única" }));
        Assert.Equal("única", picker.Pick(new[] { "única" }));
    }

    [Fact]
    public void Uses_the_key_to_recognize_items()
    {
        var items = new[] { (id: "a", n: 1), (id: "b", n: 2) };
        var picker = new RecentPicker(1, new Random(3));
        var first = picker.Pick(items, x => x.id);
        var second = picker.Pick(items, x => x.id);
        Assert.NotEqual(first.id, second.id);
    }

    [Fact]
    public void Empty_list_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => new RecentPicker(3).Pick(Array.Empty<string>()));
    }
}
