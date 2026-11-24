using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FactorySim.Core;
using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// Tab "Cijfers" of the bottom panel (plan 11): the totals of the run, a table per remover (count,
/// per minute, lead time) and a table per driven part (on-time). Refreshes at most four times a second.
/// </summary>
public partial class StatsView : VBoxContainer
{
    /// <summary>Real seconds between two refreshes.</summary>
    public const double RefreshSeconds = 0.25;

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    private readonly ProductionStats _stats;
    private readonly Func<IReadOnlyCollection<string>> _drivenTags;
    private Label _summary = null!;
    private Label _status = null!;
    private Tree _removers = null!;
    private Tree _actuators = null!;
    private double _sinceRefresh = RefreshSeconds;

    public StatsView(ProductionStats stats, Func<IReadOnlyCollection<string>> drivenTags)
    {
        _stats = stats;
        _drivenTags = drivenTags;
    }

    /// <summary>The text of the totals line as it is shown now.</summary>
    public string SummaryText => _summary.Text;

    /// <summary>Rows in the remover table and the table of driven parts, as shown now.</summary>
    public (int Removers, int Driven) RowCounts { get; private set; }

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        _summary = new Label { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddChild(_summary);
        _status = new Label { Modulate = Shell.Muted };
        bar.AddChild(_status);
        bar.AddChild(Shell.MakeButton("Exporteer CSV", () => Export(), "Schrijft de cijfers als CSV-bestand naar stats.csv"));
        AddChild(bar);

        var tables = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _removers = MakeTree(new[] { "Remover", "Aantal", "Per minuut", "Laatste 60 s", "Doorlooptijd gem.", "Kortst", "Langst" },
            new[] { 180, 70, 90, 100, 130, 80, 80 });
        tables.AddChild(_removers);
        _actuators = MakeTree(new[] { "Aangedreven onderdeel", "Aan", "Aan-tijd" }, new[] { 200, 90, 90 });
        tables.AddChild(_actuators);
        AddChild(tables);
    }

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        if (!IsVisibleInTree() || _sinceRefresh < RefreshSeconds)
        {
            return;
        }

        Refresh();
    }

    /// <summary>Fills the totals line and both tables from the figures now.</summary>
    public void Refresh()
    {
        _sinceRefresh = 0.0;
        _summary.Text = $"Looptijd {Seconds(_stats.RunTime)}   ·   gespawnd {_stats.EmittedCount}   ·   weggehaald {_stats.RemovedCount}"
            + $"   ·   gevallen {_stats.FallenCount}   ·   {Number(_stats.PerMinute, "0.0")} per minuut"
            + $" ({Number(_stats.PerMinuteWindow, "0.0")} in de laatste 60 s)";

        _removers.Clear();
        TreeItem root = _removers.CreateItem();
        IReadOnlyList<RemoverStats> removers = _stats.Removers;
        foreach (RemoverStats remover in removers)
        {
            TreeItem row = _removers.CreateItem(root);
            row.SetText(0, remover.Name);
            row.SetText(1, remover.Count.ToString(Dutch));
            row.SetText(2, Number(remover.PerMinute, "0.0"));
            row.SetText(3, Number(remover.PerMinuteWindow, "0.0"));
            row.SetText(4, Seconds(remover.LeadTimeAverage));
            row.SetText(5, Seconds(remover.LeadTimeMin));
            row.SetText(6, Seconds(remover.LeadTimeMax));
        }

        _actuators.Clear();
        root = _actuators.CreateItem();
        IReadOnlyCollection<string> driven = _drivenTags();
        int drivenRows = 0;
        foreach (ActuatorStats actuator in _stats.Actuators.Where(a => driven.Contains(a.Tag)))
        {
            TreeItem row = _actuators.CreateItem(root);
            row.SetText(0, actuator.Tag);
            row.SetText(1, Seconds(actuator.OnSeconds));
            row.SetText(2, Number(actuator.OnFraction * 100.0, "0.0") + " %");
            drivenRows++;
        }

        RowCounts = (removers.Count, drivenRows);
    }

    /// <summary>Writes the figures to user://stats.csv. Returns the path, or null.</summary>
    public string? Export()
    {
        string path = ProjectSettings.GlobalizePath("user://stats.csv");
        try
        {
            File.WriteAllText(path, _stats.ToCsv());
            _status.Text = path;
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Export mislukt: {e.Message}";
            return null;
        }
    }

    private static string Number(double value, string format) => value.ToString(format, Dutch);

    private static string Seconds(double? value) => value is { } v ? v.ToString("0.0", Dutch) + " s" : "–";

    private static Tree MakeTree(string[] titles, int[] widths)
    {
        var tree = new Tree
        {
            Columns = titles.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            FocusMode = FocusModeEnum.None,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        for (int column = 0; column < titles.Length; column++)
        {
            tree.SetColumnTitle(column, titles[column]);
            tree.SetColumnTitleAlignment(column, HorizontalAlignment.Left);
            tree.SetColumnCustomMinimumWidth(column, widths[column]);
            tree.SetColumnExpand(column, column == 0);
        }

        return tree;
    }
}
