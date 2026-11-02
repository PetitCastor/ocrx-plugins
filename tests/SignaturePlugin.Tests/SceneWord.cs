namespace SignaturePlugin.Tests;

/// <summary>One word of text on a simulated screen, with its box in frame pixels and the line it belongs to.</summary>
internal sealed record SceneWord(string Text, NumberBox Box, int Line);
