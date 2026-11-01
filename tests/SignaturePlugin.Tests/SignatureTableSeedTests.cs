using System.Text.Json.Nodes;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// The user's table is copied once and then edited by hand, so the only way a shipped entry such as
/// debris reaches an existing install is this merge. These pin both halves of its promise: a new
/// entry arrives exactly once, and nothing the user wrote is ever changed or brought back.
/// </summary>
public class SignatureTableSeedTests : IDisposable
{
    private const string Defaults = """
        {
          "tableVersion": 2,
          "entries": [
            { "name": "Ore", "signature": 100 },
            { "name": "First", "kind": "debris", "signature": 50, "addedIn": 1 },
            { "name": "Second", "kind": "debris", "signature": 70, "addedIn": 2 }
          ]
        }
        """;

    private readonly string _dir =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"sig-table-{Guid.NewGuid():N}")).FullName;

    private string TablePath => Path.Combine(_dir, "signature-table.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string[] Seed(string? existing)
    {
        if (existing is not null)
            File.WriteAllText(TablePath, existing);

        SignatureTableSeed.Ensure(TablePath, Defaults);
        return Names();
    }

    private string[] Names()
    {
        var entries = JsonNode.Parse(File.ReadAllText(TablePath))!["entries"]!.AsArray();
        return [.. entries.Select(entry => entry!["name"]!.GetValue<string>())];
    }

    private int Stamp() => JsonNode.Parse(File.ReadAllText(TablePath))!["tableVersion"]?.GetValue<int>() ?? 0;

    [Fact]
    public void FirstRun_WritesEveryEntryWithoutTheBookkeeping()
    {
        Assert.Equal(["Ore", "First", "Second"], Seed(null));
        Assert.DoesNotContain("addedIn", File.ReadAllText(TablePath));
        Assert.Equal(2, Stamp());
    }

    /// <summary>A table written before debris existed has no stamp, and gains every newer entry.</summary>
    [Fact]
    public void UnstampedTable_GainsTheNewEntriesAfterItsOwn()
    {
        var names = Seed("""{ "entries": [ { "name": "Ore", "signature": 100 } ] }""");

        Assert.Equal(["Ore", "First", "Second"], names);
        Assert.DoesNotContain("addedIn", File.ReadAllText(TablePath));
        Assert.Equal(2, Stamp());
    }

    /// <summary>
    /// Deleting an offered entry is the user's answer to the offer. Only entries newer than the stamp
    /// are considered, so First is not brought back while Second, never offered, still arrives.
    /// </summary>
    [Fact]
    public void StampedTable_DoesNotBringBackAnEntryTheUserDeleted()
    {
        var names = Seed("""{ "tableVersion": 1, "entries": [ { "name": "Ore", "signature": 100 } ] }""");

        Assert.Equal(["Ore", "Second"], names);
    }

    [Fact]
    public void CurrentTable_IsLeftByteForByte()
    {
        const string current = """{ "tableVersion": 2, "entries": [ { "name": "Mine", "signature": 1 } ] }""";

        Seed(current);

        Assert.Equal(current, File.ReadAllText(TablePath));
    }

    /// <summary>
    /// The user measured that value; the shipped entry would be a duplicate signature, which the table
    /// rejects, so the user's own entry keeps it — under the user's name and kind.
    /// </summary>
    [Fact]
    public void NewEntry_IsSkippedWhenTheUserAlreadyListsItsSignature()
    {
        var names = Seed("""{ "entries": [ { "name": "Salvage", "signature": 50 } ] }""");

        Assert.Equal(["Salvage", "Second"], names);
        Assert.Equal(2, Stamp());
    }

    [Fact]
    public void Merge_KeepsTheUsersOwnValuesAndExtraProperties()
    {
        Seed("""{ "source": "mine", "entries": [ { "name": "Ore", "signature": 101 } ] }""");

        var root = JsonNode.Parse(File.ReadAllText(TablePath))!;
        Assert.Equal("mine", root["source"]!.GetValue<string>());
        Assert.Equal(101, root["entries"]![0]!["signature"]!.GetValue<double>());
    }

    /// <summary>The promise end to end: shipped once, deleted by the user, and still gone next launch.</summary>
    [Fact]
    public void DeletedAfterFirstRun_StaysDeleted()
    {
        Seed(null);
        var root = JsonNode.Parse(File.ReadAllText(TablePath))!;
        root["entries"]!.AsArray().RemoveAt(1);
        File.WriteAllText(TablePath, root.ToJsonString());

        Assert.Equal(["Ore", "Second"], Seed(null));
    }

    [Fact]
    public void NewEntry_IsSkippedWhenTheUserAlreadyHasOneByThatName()
    {
        var names = Seed("""{ "entries": [ { "name": "first", "signature": 55 } ] }""");

        Assert.Equal(["first", "Second"], names);
    }

    /// <summary>Read as 0, a stamp of 1.0 would re-offer every entry the user already declined.</summary>
    [Theory]
    [InlineData("1.0")]
    [InlineData("\"1\"")]
    public void UnreadableStamp_IsNotTouched(string stamp)
    {
        var text = $$"""{ "tableVersion": {{stamp}}, "entries": [ { "name": "Ore", "signature": 100 } ] }""";

        Seed(text);

        Assert.Equal(text, File.ReadAllText(TablePath));
    }

    /// <summary>
    /// An existing table used to be read-only to the plugin; a file it cannot write must still load,
    /// without the new entries, rather than stopping the plugin from starting.
    /// </summary>
    [Fact]
    public void ReadOnlyTable_StillLoadsWithoutTheNewEntries()
    {
        const string text = """{ "entries": [ { "name": "Ore", "signature": 100 } ] }""";
        File.WriteAllText(TablePath, text);
        File.SetAttributes(TablePath, FileAttributes.ReadOnly);
        try
        {
            SignatureTableSeed.Ensure(TablePath, Defaults);

            Assert.Equal(text, File.ReadAllText(TablePath));
            Assert.False(File.Exists(TablePath + ".tmp"));
        }
        finally
        {
            File.SetAttributes(TablePath, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Merge_DoesNotEscapeTheNamesItRewrites()
    {
        Seed("""{ "entries": [ { "name": "Ore & Gems", "signature": 100 } ] }""");

        Assert.Contains("Ore & Gems", File.ReadAllText(TablePath));
    }

    /// <summary>A hand-edit gone wrong is reported by LoadFrom with the user's text intact, never replaced.</summary>
    [Theory]
    [InlineData("{ \"entries\": [")]
    [InlineData("{ \"entries\": {} }")]
    [InlineData("[]")]
    public void UnreadableTable_IsNotTouched(string broken)
    {
        File.WriteAllText(TablePath, broken);

        SignatureTableSeed.Ensure(TablePath, Defaults);

        Assert.Equal(broken, File.ReadAllText(TablePath));
    }
}
