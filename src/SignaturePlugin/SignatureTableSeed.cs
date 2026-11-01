using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SignaturePlugin;

/// <summary>
/// Seeds the user-editable signature table from the embedded default, and offers entries added to
/// that default since the user's file was written — each one exactly once.
/// </summary>
/// <remarks>
/// <para>
/// The table used to be copied on first run and never touched again, which left every existing
/// install without anything shipped later: debris was added to the embedded table, and a user who
/// had run the plugin once would have kept reading debris signatures as nothing at all. This is the
/// same promise <c>Ocrx.Sdk.ConfigSeed</c> makes for <c>config.json</c>, applied to <c>entries</c>:
/// each embedded entry may carry an <c>addedIn</c> version, the user's file carries the
/// <c>tableVersion</c> it was last brought forward to, and only entries newer than that stamp are
/// considered. An entry the user was already offered is never reconsidered, so deleting one is
/// permanent.
/// </para>
/// <para>
/// The merge is strictly additive. It never edits or removes a user's entry, and an offered entry
/// whose signature the user already lists is skipped rather than added beside it — the table rejects
/// duplicate signatures, and a user who measured that value has the better claim to it. A file this
/// class cannot read with confidence is left exactly as it is, so <see cref="SignatureTable.LoadFrom"/>
/// can report the problem with the user's edits intact.
/// </para>
/// <para>
/// Unlike <c>ConfigSeed</c>, a failed merge is not fatal. Before this class existed an existing table
/// was only ever read, so a read-only or locked file was harmless; the plugin keeps that property and
/// starts on the table the user already has, without the new entries, rather than not at all.
/// </para>
/// </remarks>
internal static class SignatureTableSeed
{
    private const string VersionProperty = "tableVersion";
    private const string AddedInProperty = "addedIn";
    private const string EntriesProperty = "entries";
    private const string SignatureProperty = "signature";

    private const string NameProperty = "name";

    // Relaxed escaping keeps a hand-written name such as "Ore & Gems" readable after a merge rewrites
    // the file; the default encoder would turn it into &.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Ensures <paramref name="path"/> exists, seeding it from <paramref name="defaults"/>, then adds
    /// any default entries introduced since the file was last stamped.
    /// </summary>
    public static void Ensure(string path, string defaults)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            Write(full, Seedable(defaults));
            return;
        }

        try
        {
            MergeNewEntries(full, defaults);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"signature table: could not add new default entries to '{full}': {ex.Message}");
        }
    }

    private static void MergeNewEntries(string path, string defaults)
    {
        if (TryParseObject(defaults) is not { } embedded || Entries(embedded) is not { } embeddedEntries)
            return;

        var target = ReadInt(embedded, VersionProperty);
        if (target == 0)
            return;

        if (TryParseObject(File.ReadAllText(path)) is not { } user || Entries(user) is not { } userEntries)
            return;

        // A stamp that is there but is not an integer is a hand-edit this class cannot interpret.
        // Reading it as 0 would re-offer every entry the user has already declined.
        if (FindKey(user, VersionProperty) is not null && !TryReadInt(user, VersionProperty, out _))
            return;

        var stamped = ReadInt(user, VersionProperty);
        if (stamped >= target)
            return;

        var present = userEntries.OfType<JsonObject>()
            .Select(entry => ReadDouble(entry, SignatureProperty))
            .OfType<double>()
            .ToHashSet();

        // A user who already added their own entry under the shipped name has answered for it, at
        // whatever value they measured.
        var names = userEntries.OfType<JsonObject>()
            .Select(entry => ReadString(entry, NameProperty))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in embeddedEntries.OfType<JsonObject>())
        {
            if (ReadInt(candidate, AddedInProperty) <= stamped)
                continue;

            if (ReadDouble(candidate, SignatureProperty) is not { } signature || !present.Add(signature))
                continue;

            if (ReadString(candidate, NameProperty) is { } name && !names.Add(name))
                continue;

            userEntries.Add(WithoutBookkeeping(candidate));
        }

        // Stamped even when nothing was added: the stamp records how far this file has been brought
        // forward, and the next merge reads it to know what it must not offer again.
        user[FindKey(user, VersionProperty) ?? VersionProperty] = target;
        Write(path, user.ToJsonString(WriteOptions));
    }

    /// <summary>The default as a first run should land it, without the <c>addedIn</c> bookkeeping.</summary>
    private static string Seedable(string defaults)
    {
        if (TryParseObject(defaults) is not { } embedded || Entries(embedded) is not { } entries)
            return defaults;

        var cleaned = new JsonArray();
        foreach (var node in entries)
            cleaned.Add(node is JsonObject entry ? WithoutBookkeeping(entry) : node?.DeepClone());

        embedded[FindKey(embedded, EntriesProperty)!] = cleaned;
        return embedded.ToJsonString(WriteOptions);
    }

    private static JsonNode WithoutBookkeeping(JsonObject entry)
    {
        var clone = (JsonObject)entry.DeepClone();
        if (FindKey(clone, AddedInProperty) is { } key)
            clone.Remove(key);

        return clone;
    }

    /// <summary>
    /// Writes through a temporary file and then replaces, so a crash mid-write cannot leave the user
    /// with a truncated table in place of their edits.
    /// </summary>
    private static void Write(string path, string content)
    {
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            // Only still there when the write or the move failed.
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static JsonArray? Entries(JsonObject root)
        => FindKey(root, EntriesProperty) is { } key ? root[key] as JsonArray : null;

    /// <summary>
    /// Parses to an object, or null for malformed JSON or a non-object root. Enumerating the keys
    /// forces the <see cref="ArgumentException"/> a duplicate key otherwise throws on first access.
    /// </summary>
    private static JsonObject? TryParseObject(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject parsed)
                return null;

            _ = parsed.Select(pair => pair.Key).ToArray();
            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static int ReadInt(JsonObject node, string name)
        => TryReadInt(node, name, out var result) ? result : 0;

    private static bool TryReadInt(JsonObject node, string name, out int result)
    {
        result = 0;
        return FindKey(node, name) is { } key && node[key] is JsonValue value && value.TryGetValue(out result);
    }

    private static string? ReadString(JsonObject node, string name)
        => FindKey(node, name) is { } key && node[key] is JsonValue value && value.TryGetValue(out string? result)
            ? result
            : null;

    private static double? ReadDouble(JsonObject node, string name)
        => FindKey(node, name) is { } key && node[key] is JsonValue value && value.TryGetValue(out double result)
            ? result
            : null;

    private static string? FindKey(JsonObject node, string name)
        => node.Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
}
