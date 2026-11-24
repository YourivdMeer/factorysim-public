namespace FactorySim.Core;

/// <summary>
/// An operator control with one contact: push button, emergency stop or selector.
/// A momentary control is engaged while it is held; a latching control changes over
/// on every press and stays there. A normally-closed contact gives a signal at rest
/// and loses it when the control is engaged, like a real emergency stop.
/// </summary>
public sealed class ButtonLogic
{
    private readonly bool _latching;
    private readonly bool _normallyClosed;

    public ButtonLogic(bool latching, bool normallyClosed)
    {
        _latching = latching;
        _normallyClosed = normallyClosed;
    }

    public bool Latching => _latching;

    /// <summary>True while the control is pushed in or switched over.</summary>
    public bool Engaged { get; private set; }

    /// <summary>What the PLC input sees.</summary>
    public bool Signal => Engaged != _normallyClosed;

    public void Press() => Engaged = !_latching || !Engaged;

    public void Release()
    {
        if (!_latching)
        {
            Engaged = false;
        }
    }

    public void Reset() => Engaged = false;
}
