using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 35, part of the "ui" self-test: at a window of 1600, 1366 and 1280 px the toolbar fits in both modes, and in
/// design mode both side panels are in the window. Each size waits a few frames for the layout; afterwards the window
/// is 1600 x 900 again, in simulate mode.
/// </summary>
public partial class SelfTest
{
    private static readonly int[] ToolbarWidths = { 1600, 1366, 1280 };

    private int _toolbarStep;
    private int _toolbarFrames;

    /// <summary>One frame of the toolbar check; true when it is done.</summary>
    private bool RunUiToolbarFit()
    {
        int count = ToolbarWidths.Length * 2;
        if (_toolbarStep >= count)
        {
            if (_toolbarFrames++ == 0)
            {
                GetTree().Root.Size = new Vector2I(1600, 900);
                Editor.SetDesignMode(false);
            }

            return _toolbarFrames > 10;      // let the rebuilt scene settle before the test quits
        }

        int width = ToolbarWidths[_toolbarStep / 2];
        bool design = _toolbarStep % 2 == 1;
        if (_toolbarFrames == 0)
        {
            GetTree().Root.Size = new Vector2I(width, 800);
            Editor.SetDesignMode(design);
        }

        if (++_toolbarFrames < 4)
        {
            return false;      // layout is deferred: give it a few frames
        }

        Ui.Shell shell = Main.Shell;
        Vector2 window = GetViewport().GetVisibleRect().Size;
        Rect2 left = shell.LeftPanelRect;
        Rect2 right = shell.RightPanelRect;
        float need = shell.Toolbar.GetCombinedMinimumSize().X;
        bool fits = shell.LayoutWidth <= window.X + 0.5f;
        bool panels = !design || (left.Position.X >= 0.0f && right.End.X <= window.X + 0.5f && shell.FreeRect.Size.X > 500.0f);
        string mode = design ? "design" : "simulate";
        GD.Print($"SELFTEST ui toolbar at {width} px ({mode}): window={window} layout={shell.LayoutWidth} toolbar min width={need} left={left} right={right}");
        SortersCheck($"toolbar fits at {width} px in {mode} mode", fits && panels);

        _toolbarStep++;
        _toolbarFrames = 0;
        return false;
    }

    /// <summary>Plan 35: Help ▾ has Aan de slag, the shortcuts and Over, and each opens its window.</summary>
    private void RunUiHelpMenu()
    {
        Ui.Hud hud = Main.Hud;
        PopupMenu menu = hud.HelpMenu.GetPopup();
        bool items = menu.ItemCount == 3
            && menu.GetItemText(menu.GetItemIndex(Ui.Hud.HelpWelcome)) == "Aan de slag"
            && menu.GetItemText(menu.GetItemIndex(Ui.Hud.HelpAbout)) == "Over FactorySim";
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, (long)Ui.Hud.HelpWelcome);
        bool welcome = hud.WelcomeWindow.Visible;
        hud.WelcomeWindow.Hide();
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, (long)Ui.Hud.HelpAbout);
        bool about = Main.About.Visible;
        Main.About.Hide();
        GD.Print($"SELFTEST ui help menu: items={items} welcome={welcome} about={about}");
        SortersCheck("Help menu opens Aan de slag and Over", items && welcome && about);
    }
}
