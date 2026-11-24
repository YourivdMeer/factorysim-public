using System;

namespace FactorySim.Core.Scenes;

/// <summary>One automatic copy of a scene: its file name, the scene it belongs to and when it was written.</summary>
public sealed record AutosaveCopy(string FileName, string Key, DateTime Time);
