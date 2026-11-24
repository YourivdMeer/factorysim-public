using Godot;

namespace FactorySim.App;

/// <summary>Plan 25, part of the "ui" self-test: the app icon imports as a 512 x 512 texture.</summary>
public partial class SelfTest
{
    private void RunUiBranding()
    {
        var icon = ResourceLoader.Load<Texture2D>("res://branding/icon.svg");
        SortersCheck("app icon loads as 512 x 512", icon is not null && icon.GetWidth() == 512 && icon.GetHeight() == 512);
    }
}
