using FactorySim.Core;

namespace FactorySim.Core.Tests;

public class ButtonLogicTests
{
    [Fact]
    public void A_momentary_button_signals_only_while_held()
    {
        var button = new ButtonLogic(latching: false, normallyClosed: false);
        Assert.False(button.Signal);

        button.Press();
        Assert.True(button.Engaged);
        Assert.True(button.Signal);

        button.Release();
        Assert.False(button.Engaged);
        Assert.False(button.Signal);
    }

    [Fact]
    public void A_latching_control_toggles_on_every_press_and_ignores_release()
    {
        var selector = new ButtonLogic(latching: true, normallyClosed: false);

        selector.Press();
        selector.Release();
        Assert.True(selector.Signal);

        selector.Press();
        selector.Release();
        Assert.False(selector.Signal);
    }

    [Fact]
    public void A_normally_closed_contact_signals_until_the_control_is_engaged()
    {
        var emergencyStop = new ButtonLogic(latching: true, normallyClosed: true);
        Assert.True(emergencyStop.Signal);

        emergencyStop.Press();
        emergencyStop.Release();
        Assert.True(emergencyStop.Engaged);
        Assert.False(emergencyStop.Signal);

        emergencyStop.Press();
        Assert.True(emergencyStop.Signal);
    }

    [Fact]
    public void Reset_returns_the_control_to_rest()
    {
        var selector = new ButtonLogic(latching: true, normallyClosed: false);
        selector.Press();

        selector.Reset();

        Assert.False(selector.Engaged);
        Assert.False(selector.Signal);
    }
}
