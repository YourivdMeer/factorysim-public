namespace FactorySim.Core.V23;

/// <summary>How the body of a POU is written, as far as the container can tell from its first line.</summary>
public enum ExpBodyKind
{
    /// <summary>No body: a type, a variable list or a project-level block.</summary>
    None,

    /// <summary>Plain text: ST, or IL (the container does not tell them apart; IL is not confirmed yet).</summary>
    Text,

    /// <summary>Ladder: a token stream that starts with <c>_LD_BODY</c>.</summary>
    Ld,

    /// <summary>Function block diagram: a token stream that starts with <c>_FBD_BODY</c>.</summary>
    Fbd,

    /// <summary>Sequential function chart as text: <c>INITIAL_STEP</c>, <c>STEP</c>, <c>TRANSITION</c>.</summary>
    Sfc,

    /// <summary>Another <c>_…_BODY</c> marker, for example CFC, whose layout is not known yet.</summary>
    Unknown,
}
