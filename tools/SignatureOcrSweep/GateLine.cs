namespace SignatureOcrSweep;

/// <summary>One frame x knob set in the gate: what the engine read against what offline mode read.</summary>
public sealed record GateLine(string Frame, string Configuration, string KnobSet, string Engine, string Offline)
{
    public bool Match => Engine == Offline;
}
