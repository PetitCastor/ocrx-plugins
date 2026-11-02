namespace SignaturePlugin;

/// <summary>Which edge of the counter's rect has sliced into the number.</summary>
internal enum CounterCut
{
    None,

    /// <summary>The first digit (or part of it) is outside the rect: "17,140" reads as "7,140", which is a valid
    /// number and a different signature.</summary>
    Left,

    /// <summary>The last digits are outside the rect: "17,200" reads as "17,20" or "17,".</summary>
    Right,
}
