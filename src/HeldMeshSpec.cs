using System.Collections.Generic;
using UnityEngine;

namespace CustomItems;

/// <summary>
/// Everything the shared <see cref="HeldMeshVisual"/> needs to render a camera-tracked first-person mesh:
/// the primitives, where it sits in the holder's camera space, its scale, and an optional pulsing core light.
/// Build one per "show" — it carries no runtime state.
/// </summary>
public sealed class HeldMeshSpec
{
    public HeldMeshSpec(IReadOnlyList<MeshPrimitive> primitives, Vector3 cameraOffset, float scale, HeldLightSpec? light = null)
    {
        Primitives = primitives;
        CameraOffset = cameraOffset;
        Scale = scale;
        Light = light;
    }

    /// <summary>The mesh primitives (mesh-local coords; markers are skipped). See <see cref="MeshPrimitive"/>.</summary>
    public IReadOnlyList<MeshPrimitive> Primitives { get; }

    /// <summary>Offset from the camera (camera-space) at which the mesh centre is placed each frame.</summary>
    public Vector3 CameraOffset { get; }

    /// <summary>Uniform scale of the mesh root (clamped to a small minimum at spawn).</summary>
    public float Scale { get; }

    /// <summary>Optional pulsing point light at the mesh core; null for no light.</summary>
    public HeldLightSpec? Light { get; }
}

/// <summary>A pulsing point light placed at the held mesh's core.</summary>
public sealed class HeldLightSpec
{
    public HeldLightSpec(Color color, Vector3 localPosition, float baseIntensity = 1.1f, float pulseAmplitude = 0.6f, float pulseHz = 1.0f, float range = 1.6f, string? anchorMarker = null)
    {
        Color = color;
        LocalPosition = localPosition;
        BaseIntensity = baseIntensity;
        PulseAmplitude = pulseAmplitude;
        PulseHz = pulseHz;
        Range = range;
        AnchorMarker = anchorMarker;
    }

    public Color Color { get; }

    /// <summary>Light position relative to the (bounding-box-centred) mesh root.</summary>
    public Vector3 LocalPosition { get; }

    public float BaseIntensity { get; }

    public float PulseAmplitude { get; }

    public float PulseHz { get; }

    public float Range { get; }

    /// <summary>
    /// If set and a matching <see cref="MeshPrimitive"/> exists, the light is positioned at that marker
    /// (relative to the mesh centre) instead of <see cref="LocalPosition"/>.
    /// </summary>
    public string? AnchorMarker { get; }
}
