using System;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Camera that orbits a pivot: right mouse button drag rotates, the wheel zooms. The pivot is
/// drawn in the middle of the part of the window that the panels leave free
/// (<see cref="FreeRect"/>), not in the middle of the window.
/// </summary>
public partial class OrbitCamera : Camera3D
{
    private const float RotateSpeed = 0.006f;
    private const float ZoomStep = 0.9f;
    private const float MinDistance = (float)FactorySim.Core.Settings.AppSettings.MinCameraDistance;
    private const float MaxDistance = (float)FactorySim.Core.Settings.AppSettings.MaxCameraDistance;

    private float _yaw = Mathf.DegToRad(-35.0f);
    private float _pitch = Mathf.DegToRad(-32.0f);

    public Vector3 Pivot { get; set; } = Vector3.Zero;

    public float Distance { get; set; } = 7.0f;

    /// <summary>The free part of the window, in pixels; an empty rectangle means the whole window.</summary>
    public Rect2 FreeRect { get; set; }

    public float YawDegrees => Mathf.RadToDeg(_yaw);

    public float PitchDegrees => Mathf.RadToDeg(_pitch);

    public override void _Ready() => UpdateTransform();

    public override void _Process(double delta) => UpdateTransform();

    public void Frame(Vector3 pivot, float distance)
    {
        Pivot = pivot;
        Distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        UpdateTransform();
    }

    /// <summary>Looks from a fixed direction: yaw around the vertical axis, pitch down from the horizon (negative).</summary>
    public void SetView(float yawDegrees, float pitchDegrees)
    {
        _yaw = Mathf.DegToRad(yawDegrees);
        _pitch = Mathf.DegToRad(Mathf.Clamp(pitchDegrees, (float)FactorySim.Core.Settings.AppSettings.MinCameraPitch, (float)FactorySim.Core.Settings.AppSettings.MaxCameraPitch));
        UpdateTransform();
    }

    /// <summary>
    /// Asked first for every notch of the mouse wheel, with the mouse position and +1 (up) or −1 (down).
    /// Returns true when it used the notch (turned a knob under the mouse); the camera then does not zoom.
    /// </summary>
    public Func<Vector2, int, bool>? WheelOverride { get; set; }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel
            && WheelOverride?.Invoke(wheel.Position, wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1) == true)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (@event)
        {
            case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Right):
                _yaw -= motion.Relative.X * RotateSpeed;
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * RotateSpeed, Mathf.DegToRad(-89.0f), Mathf.DegToRad(-2.0f));
                UpdateTransform();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Distance = Mathf.Max(MinDistance, Distance * ZoomStep);
                UpdateTransform();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Distance = Mathf.Min(MaxDistance, Distance / ZoomStep);
                UpdateTransform();
                break;
        }
    }

    private void UpdateTransform()
    {
        var rotation = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);
        Position = Pivot + rotation * new Vector3(0.0f, 0.0f, Distance);
        // Straight down, "up" would be parallel to the view direction: use the view's own up instead.
        LookAt(Pivot, Mathf.Abs(_pitch) > Mathf.DegToRad(88.0f) ? rotation * Vector3.Up : Vector3.Up);

        // Shift the frustum so the pivot lands in the middle of the free rectangle.
        Vector2 window = IsInsideTree() ? GetViewport().GetVisibleRect().Size : Vector2.Zero;
        if (window.Y < 200.0f || FreeRect.Size.X < 50.0f || FreeRect.Size.Y < 50.0f)
        {
            HOffset = 0.0f;
            VOffset = 0.0f;
            return;
        }

        Vector2 shift = FreeRect.GetCenter() - window / 2.0f;
        float worldPerPixel = Distance * Mathf.Tan(Mathf.DegToRad(Fov) / 2.0f) / (window.Y / 2.0f);
        HOffset = -shift.X * worldPerPixel;
        VOffset = shift.Y * worldPerPixel;
    }
}
