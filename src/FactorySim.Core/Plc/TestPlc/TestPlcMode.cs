namespace FactorySim.Core.Plc.TestPlc;

/// <summary>What the built-in test PLC runs; the same modes as <c>tools/fake_plc/fake_plc.py</c>.</summary>
public enum TestPlcMode
{
    /// <summary>Start/stop and sort on height for the MVP scene (fixed coil and discrete input offsets).</summary>
    Sorter,

    /// <summary>Every coil comes back on the discrete input with the same offset, every holding register on the input register.</summary>
    Echo,
}
