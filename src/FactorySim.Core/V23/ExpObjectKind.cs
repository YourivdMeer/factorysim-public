namespace FactorySim.Core.V23;

/// <summary>What a top-level object in a CoDeSys V2.3 <c>.exp</c> file is.</summary>
public enum ExpObjectKind
{
    Program,
    FunctionBlock,
    Function,

    /// <summary><c>TYPE … END_TYPE</c>: an enum, struct or alias.</summary>
    Type,

    /// <summary>A global variable list: <c>VAR_GLOBAL</c> blocks up to <c>@OBJECT_END</c>.</summary>
    GlobalVariableList,

    /// <summary>The variable configuration: <c>VAR_CONFIG</c> up to <c>@OBJECT_END</c>.</summary>
    VariableConfiguration,

    /// <summary><c>LIBRARY … END_LIBRARY</c>: the names of a library's POUs, without their code.</summary>
    Library,

    /// <summary><c>PLC_CONFIGURATION … PLC_END</c>: modules and their I/O addresses.</summary>
    PlcConfiguration,

    /// <summary><c>RESOURCE … END_RESOURCE</c>: the task configuration and system events.</summary>
    Resource,

    /// <summary><c>_ALARMCONFIG … _END_ALARMCONFIG</c>.</summary>
    AlarmConfiguration,

    /// <summary><c>_WORKSPACE … _END_WORKSPACE</c>.</summary>
    Workspace,

    /// <summary>A block the parser does not know; it is kept and reported in a warning.</summary>
    Unknown,
}
