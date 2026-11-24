using FactorySim.Core.V23;

namespace FactorySim.Core.Tests.V23;

/// <summary>
/// The container parser of spoor B (B0): it only splits a CoDeSys V2.3 <c>.exp</c> file into its
/// objects. The snippets here are written for these tests; they follow the structure of the real
/// exports, without copying them.
/// </summary>
public class ExpContainerTests
{
    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly string[] PouHeader =
    {
        "(* @NESTEDCOMMENTS := 'Yes' *)",
        "(* @PATH := '\\/Logic\\/Door' *)",
        "(* @OBJECTFLAGS := '0, 8' *)",
        "(* @SYMFILEFLAGS := '2048' *)",
    };

    private static string Program(string name, params string[] body) => Lines(PouHeader.Concat(new[]
    {
        $"PROGRAM {name}",
        "VAR",
        "    x : BOOL;",
        "END_VAR",
        "(* @END_DECLARATION := '0' *)",
    }).Concat(body).Append("END_PROGRAM").ToArray());

    [Fact]
    public void A_program_is_one_object_with_its_name_path_and_metadata()
    {
        ExpContainer container = ExpContainer.Parse(Program("Door", "x := TRUE;"));

        ExpObject pou = Assert.Single(container.Objects);
        Assert.Equal(ExpObjectKind.Program, pou.Kind);
        Assert.Equal("Door", pou.Name);
        Assert.Equal("/Logic/Door", pou.Path);
        Assert.Equal("0, 8", pou.Metadata["OBJECTFLAGS"]);
        Assert.Equal(1, pou.FirstLine);
        Assert.Equal(11, pou.LastLine);
        Assert.Empty(container.Warnings);
    }

    [Fact]
    public void The_declaration_and_the_body_are_split_at_end_declaration()
    {
        ExpObject pou = Assert.Single(ExpContainer.Parse(Program("Door", "x := TRUE;", "x := NOT x;")).Objects);

        Assert.Equal("PROGRAM Door\nVAR\n    x : BOOL;\nEND_VAR", pou.Declaration);
        Assert.Equal("x := TRUE;\nx := NOT x;", pou.Body);
        Assert.Equal(ExpBodyKind.Text, pou.BodyKind);
    }

    [Fact]
    public void Function_blocks_functions_and_types_have_their_own_kind()
    {
        string text = Lines(
            "(* @NESTEDCOMMENTS := 'Yes' *)",
            "(* @PATH := '\\/Types' *)",
            "(* @OBJECTFLAGS := '0, 8' *)",
            "TYPE eMode :",
            "(Idle, Run);",
            "END_TYPE",
            "(* @END_DECLARATION := '0' *)",
            "",
            "(* @NESTEDCOMMENTS := 'Yes' *)",
            "(* @PATH := '' *)",
            "FUNCTION Half : REAL",
            "VAR_INPUT",
            "    v : REAL;",
            "END_VAR",
            "(* @END_DECLARATION := '0' *)",
            "Half := v / 2.0;",
            "END_FUNCTION",
            "",
            "(* @NESTEDCOMMENTS := 'Yes' *)",
            "(* @PATH := '' *)",
            "FUNCTION_BLOCK FB_Gate",
            "VAR_OUTPUT",
            "    open : BOOL;",
            "END_VAR",
            "(* @END_DECLARATION := '0' *)",
            ";",
            "END_FUNCTION_BLOCK");

        ExpContainer container = ExpContainer.Parse(text);

        Assert.Equal(new[] { ExpObjectKind.Type, ExpObjectKind.Function, ExpObjectKind.FunctionBlock }, container.Objects.Select(o => o.Kind));
        Assert.Equal(new[] { "eMode", "Half", "FB_Gate" }, container.Objects.Select(o => o.Name));
        ExpObject type = container.Objects[0];
        Assert.Equal("TYPE eMode :\n(Idle, Run);\nEND_TYPE", type.Declaration);
        Assert.Equal(ExpBodyKind.None, type.BodyKind);
        Assert.Equal(7, type.LastLine);
        Assert.Equal("Half := v / 2.0;", container.Objects[1].Body);
        Assert.Empty(container.Warnings);
    }

    [Fact]
    public void A_global_variable_list_runs_to_its_object_end_and_connections()
    {
        string text = Lines(
            "(* @NESTEDCOMMENTS := 'Yes' *)",
            "(* @GLOBAL_VARIABLE_LIST := 'Globals' *)",
            "(* @PATH := '' *)",
            "VAR_GLOBAL",
            "    start AT %IX0.0 : BOOL;",
            "END_VAR",
            "VAR_GLOBAL CONSTANT",
            "    limit : INT := 3;",
            "END_VAR",
            "(* @OBJECT_END := 'Globals' *)",
            "(* @CONNECTIONS := Globals",
            "FILENAME : ''",
            "NUMOFCONNECTIONS : 0",
            "*)");

        ExpObject gvl = Assert.Single(ExpContainer.Parse(text).Objects);

        Assert.Equal(ExpObjectKind.GlobalVariableList, gvl.Kind);
        Assert.Equal("Globals", gvl.Name);
        Assert.Equal(14, gvl.LastLine);
        Assert.Equal("VAR_GLOBAL\n    start AT %IX0.0 : BOOL;\nEND_VAR\nVAR_GLOBAL CONSTANT\n    limit : INT := 3;\nEND_VAR", gvl.Declaration);
    }

    [Fact]
    public void A_variable_configuration_is_its_own_kind()
    {
        string text = Lines(
            "(* @GLOBAL_VARIABLE_LIST := 'Variable_Configuration' *)",
            "VAR_CONFIG",
            "END_VAR",
            "",
            "(* @OBJECT_END := 'Variable_Configuration' *)");

        ExpObject config = Assert.Single(ExpContainer.Parse(text).Objects);

        Assert.Equal(ExpObjectKind.VariableConfiguration, config.Kind);
        Assert.Equal("Variable_Configuration", config.Name);
        Assert.Equal(5, config.LastLine);
    }

    [Fact]
    public void A_library_block_is_named_after_its_file()
    {
        string text = Lines(
            "LIBRARY",
            "Standard.lib 2.12.10 14:48:34",
            "(* @LIBRARYSYMFILEINFO := '0' *)",
            "NumOfPOUs: 1",
            "TON: 0",
            "NumOfGVLs: 0",
            "END_LIBRARY",
            "",
            "LIBRARY",
            "Util.lib 2.12.10 14:48:34",
            "END_LIBRARY");

        ExpContainer container = ExpContainer.Parse(text);

        Assert.Equal(new[] { "Standard.lib", "Util.lib" }, container.Objects.Select(o => o.Name));
        Assert.All(container.Objects, o => Assert.Equal(ExpObjectKind.Library, o.Kind));
        Assert.Equal((1, 7), (container.Objects[0].FirstLine, container.Objects[0].LastLine));
    }

    [Fact]
    public void Project_level_blocks_run_to_their_end_keyword()
    {
        string text = Lines(
            "_ALARMCONFIG",
            "_ALARMCONFIGNEXTTEXTID : 10002",
            "(* @ALARMCONFIGFLAGS := '_ALARMCONFIGFLAGS: 4' *)",
            "_END_ALARMCONFIG",
            "PLC_CONFIGURATION",
            "_GLOBAL",
            "_END_GLOBAL",
            "_MODULE: '3S'",
            "_END_MODULE",
            "PLC_END",
            "RESOURCE",
            "{event_task : 'start','Called when program starts','',''}",
            "END_RESOURCE",
            "_WORKSPACE",
            "_END_WORKSPACE");

        ExpContainer container = ExpContainer.Parse(text);

        Assert.Equal(
            new[] { ExpObjectKind.AlarmConfiguration, ExpObjectKind.PlcConfiguration, ExpObjectKind.Resource, ExpObjectKind.Workspace },
            container.Objects.Select(o => o.Kind));
        Assert.Equal(new[] { (1, 4), (5, 10), (11, 13), (14, 15) }, container.Objects.Select(o => (o.FirstLine, o.LastLine)));
        Assert.Empty(container.Warnings);
    }

    [Fact]
    public void An_unknown_block_is_kept_named_and_reported()
    {
        string text = Program("Door", ";") + Lines("", "_STRANGE_BLOCK", "_SOMETHING : 1", "_END_STRANGE_BLOCK");

        ExpContainer container = ExpContainer.Parse(text);

        ExpObject unknown = container.Objects[1];
        Assert.Equal(ExpObjectKind.Unknown, unknown.Kind);
        Assert.Equal("_STRANGE_BLOCK", unknown.Name);
        Assert.Equal((13, 15), (unknown.FirstLine, unknown.LastLine));
        ExpWarning warning = Assert.Single(container.Warnings);
        Assert.Equal(13, warning.Line);
        Assert.Contains("_STRANGE_BLOCK", warning.Message);
    }

    [Fact]
    public void An_unknown_line_without_an_end_is_one_unknown_object()
    {
        ExpContainer container = ExpContainer.Parse(Lines("SOMETHING_ELSE 12", "", "LIBRARY", "Util.lib", "END_LIBRARY"));

        Assert.Equal(new[] { ExpObjectKind.Unknown, ExpObjectKind.Library }, container.Objects.Select(o => o.Kind));
        Assert.Equal((1, 1), (container.Objects[0].FirstLine, container.Objects[0].LastLine));
        Assert.Single(container.Warnings);
    }

    [Theory]
    [InlineData("_LD_BODY", ExpBodyKind.Ld)]
    [InlineData("_FBD_BODY", ExpBodyKind.Fbd)]
    [InlineData("INITIAL_STEP Idle:", ExpBodyKind.Sfc)]
    [InlineData("x := TRUE;", ExpBodyKind.Text)]
    [InlineData("_CFC_BODY", ExpBodyKind.Unknown)]
    public void The_body_kind_comes_from_the_first_line_of_the_body(string firstLine, ExpBodyKind expected)
    {
        ExpObject pou = Assert.Single(ExpContainer.Parse(Program("Door", "", firstLine, "more")).Objects);

        Assert.Equal(expected, pou.BodyKind);
    }

    [Fact]
    public void A_block_without_its_end_runs_to_the_end_of_the_file_and_is_reported()
    {
        ExpContainer container = ExpContainer.Parse(Lines("LIBRARY", "Util.lib", "NumOfPOUs: 0"));

        ExpObject library = Assert.Single(container.Objects);
        Assert.Equal(3, library.LastLine);
        ExpWarning warning = Assert.Single(container.Warnings);
        Assert.Equal(1, warning.Line);
        Assert.Contains("END_LIBRARY", warning.Message);
    }

    [Fact]
    public void A_program_without_end_declaration_is_reported()
    {
        string text = Lines("PROGRAM Door", "VAR", "END_VAR", "x := TRUE;", "END_PROGRAM");

        ExpContainer container = ExpContainer.Parse(text);

        Assert.Equal("Door", Assert.Single(container.Objects).Name);
        Assert.Contains("@END_DECLARATION", Assert.Single(container.Warnings).Message);
    }

    [Fact]
    public void Metadata_at_the_end_of_the_file_without_an_object_is_reported()
    {
        ExpContainer container = ExpContainer.Parse(Program("Door", ";") + Lines("(* @NESTEDCOMMENTS := 'Yes' *)"));

        Assert.Single(container.Objects);
        Assert.Equal(12, Assert.Single(container.Warnings).Line);
    }

    [Fact]
    public void Lf_and_crlf_line_endings_give_the_same_objects()
    {
        string crlf = Program("Door", "x := TRUE;");
        string lf = crlf.Replace("\r\n", "\n");

        ExpObject a = Assert.Single(ExpContainer.Parse(crlf).Objects);
        ExpObject b = Assert.Single(ExpContainer.Parse(lf).Objects);

        Assert.Equal((a.Name, a.LastLine, a.Body), (b.Name, b.LastLine, b.Body));
    }
}
