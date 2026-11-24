using System;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// An alarm siren: while its actuator tag is on it flashes red and sounds a two-tone wail, made on the
/// fly with an AudioStreamGenerator (no sound file). The toolbar can mute all sirens with <see cref="Muted"/>.
/// </summary>
public partial class Siren : Node3D
{
    private const float MixRate = 22050.0f;
    private const double FlashSeconds = 0.25;

    private StandardMaterial3D? _material;
    private Color _colour = new(1.0f, 0.1f, 0.05f);
    private AudioStreamPlayer3D? _player;
    private AudioStreamGeneratorPlayback? _playback;
    private double _phase;
    private double _time;
    private bool _on;

    /// <summary>True mutes every siren in the scene; set from the toolbar.</summary>
    public static bool Muted { get; set; }

    /// <summary>Name of the model in the library; it must have a Siren part.</summary>
    public string ModelName { get; set; } = "alarm_siren";

    public bool On => _on;

    /// <summary>True while the light of a running siren is in its bright half.</summary>
    public bool Lit { get; private set; }

    /// <summary>True while a tone is being generated: the siren is on and not muted.</summary>
    public bool Sounding => _on && !Muted;

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        MeshInstance3D body = Models.Part(model, "Siren");
        _material = Models.OwnMaterial(body);
        _colour = _material.AlbedoColor;
        _player = new AudioStreamPlayer3D
        {
            Name = "Tone",
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f },
            UnitSize = 4.0f,
            VolumeDb = -12.0f,
        };
        AddChild(_player);
    }

    public void Set(bool on)
    {
        if (_on == on)
        {
            return;
        }

        _on = on;
        _time = 0.0;
        if (!on)
        {
            Light(false);
        }
    }

    public void Reset() => Set(false);

    public override void _Process(double delta)
    {
        if (_on)
        {
            _time += delta;
            Light((int)(_time / FlashSeconds) % 2 == 0);
        }

        UpdateSound();
    }

    private void Light(bool lit)
    {
        Lit = lit;
        if (_material is null)
        {
            return;
        }

        if (_on)
        {
            Models.SetGlow(_material, _colour, lit);
        }
        else
        {
            // Switched off: back to the plain red of the model, not the dark half of the flash.
            _material.EmissionEnergyMultiplier = 0.0f;
            _material.AlbedoColor = _colour;
        }
    }

    private void UpdateSound()
    {
        if (_player is null)
        {
            return;
        }

        if (!Sounding)
        {
            if (_player.Playing)
            {
                _player.Stop();
                _playback = null;
            }

            return;
        }

        if (!_player.Playing)
        {
            _player.Play();
            _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        }

        if (_playback is null)
        {
            return;
        }

        // Two tones of 600 and 900 Hz that alternate every half second.
        int frames = _playback.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            double frequency = (int)(_time * 2.0) % 2 == 0 ? 600.0 : 900.0;
            _phase = (_phase + frequency / MixRate) % 1.0;
            float sample = (float)Math.Sin(_phase * Math.Tau) * 0.5f;
            _playback.PushFrame(new Vector2(sample, sample));
        }
    }
}
