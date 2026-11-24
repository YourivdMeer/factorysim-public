using System;
using FactorySim.Core.Api;
using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// Plan 21: the box Test-API in the PLC window. On/off, port, the token with Kopieer, and the status.
/// The server itself lives in Main; the HUD only calls these delegates.
/// </summary>
public partial class Hud
{
    private CheckButton _apiToggle = null!;
    private SpinBox _apiPortSpin = null!;
    private LineEdit _apiTokenEdit = null!;
    private Button _apiCopyButton = null!;
    private Label _apiLabel = null!;
    private string? _apiError;
    private bool _apiUpdating;

    /// <summary>Starts the test API on a port; returns an error text, or null.</summary>
    public Func<int, string?> StartApi { get; init; } = _ => "Test-API niet beschikbaar.";

    public Action StopApi { get; init; } = () => { };

    /// <summary>The running server, or null when the API is off.</summary>
    public Func<TestApiServer?> GetApi { get; init; } = () => null;

    /// <summary>The token of the running server, or null.</summary>
    public Func<string?> GetApiToken { get; init; } = () => null;

    /// <summary>The check box Test-API, for the self-test.</summary>
    public CheckButton ApiToggle => _apiToggle;

    /// <summary>The status line under the box, for the self-test.</summary>
    public string ApiStatusText => _apiLabel.Text;

    private void BuildApiTools(VBoxContainer column)
    {
        column.AddChild(new HSeparator());
        column.AddChild(Shell.Heading("Test-API (alleen 127.0.0.1)"));
        var row = new HBoxContainer();
        _apiToggle = new CheckButton
        {
            Text = "Aan",
            TooltipText = "Een script op deze pc kan de simulatie dan besturen (zie SCENES.md, Test-API)",
        };
        _apiToggle.Toggled += OnApiToggled;
        row.AddChild(_apiToggle);
        row.AddChild(new Label { Text = "Poort" });
        _apiPortSpin = new SpinBox { MinValue = 1, MaxValue = 65535, Value = TestApiServer.DefaultPort, TooltipText = "Poort van de test-API" };
        row.AddChild(_apiPortSpin);
        column.AddChild(row);

        var tokenRow = new HBoxContainer();
        tokenRow.AddChild(new Label { Text = "Token" });
        _apiTokenEdit = new LineEdit
        {
            Editable = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            PlaceholderText = "nieuw bij elke start",
            TooltipText = $"Stuur mee in de header {ApiToken.HeaderName}; staat ook in {Main.ApiTokenPath}",
        };
        tokenRow.AddChild(_apiTokenEdit);
        _apiCopyButton = Shell.MakeButton("Kopieer", () => DisplayServer.ClipboardSet(GetApiToken() ?? string.Empty), "Token naar het klembord");
        tokenRow.AddChild(_apiCopyButton);
        column.AddChild(tokenRow);

        _apiLabel = new Label
        {
            Modulate = OffColor,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(400.0f, 22.0f),
        };
        column.AddChild(_apiLabel);
        UpdateApiStatus();
    }

    private void OnApiToggled(bool on)
    {
        if (_apiUpdating)
        {
            return;
        }

        _apiError = null;
        if (on)
        {
            _apiError = StartApi((int)_apiPortSpin.Value);
        }
        else
        {
            StopApi();
        }

        UpdateApiStatus();
    }

    /// <summary>Follows the server, also when --api-port= started it.</summary>
    private void UpdateApiStatus()
    {
        TestApiServer? api = GetApi();
        _apiUpdating = true;
        _apiToggle.ButtonPressed = api is not null;
        _apiUpdating = false;
        _apiPortSpin.Editable = api is null;
        string token = GetApiToken() ?? string.Empty;
        if (_apiTokenEdit.Text != token)
        {
            _apiTokenEdit.Text = token;
        }

        _apiCopyButton.Disabled = api is null;
        if (api is not null)
        {
            _apiLabel.Text = $"Luistert op 127.0.0.1:{api.Port}";
            _apiLabel.Modulate = Shell.Good;
        }
        else if (_apiError is not null)
        {
            _apiLabel.Text = _apiError;
            _apiLabel.Modulate = Shell.Warn;
        }
        else
        {
            _apiLabel.Text = "Uit";
            _apiLabel.Modulate = OffColor;
        }
    }
}
