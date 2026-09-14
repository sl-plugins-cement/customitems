using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
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
        : this(primitives, cameraOffset, scale, false, light)
    {
    }

    /// <summary>Opt-in authored grip origin and separate observer presentation. The original constructor remains binary-compatible.</summary>
    public HeldMeshSpec(IReadOnlyList<MeshPrimitive> primitives, Vector3 cameraOffset, float scale,
        bool preserveAuthoredOrigin, HeldLightSpec? light = null, Vector3? rotationEuler = null,
        HeldMeshWorldSpec? world = null, Func<Player, HeldMeshPresentation>? presentationFactory = null)
    {
        Primitives = primitives;
        CameraOffset = cameraOffset;
        Scale = scale;
        Light = light;
        PreserveAuthoredOrigin = preserveAuthoredOrigin;
        RotationEuler = rotationEuler ?? Vector3.zero;
        World = world;
        PresentationFactory = presentationFactory;
    }

    /// <summary>The mesh primitives (mesh-local coords; markers are skipped). See <see cref="MeshPrimitive"/>.</summary>
    public IReadOnlyList<MeshPrimitive> Primitives { get; }

    /// <summary>Offset from the camera (camera-space) at which the mesh centre is placed each frame.</summary>
    public Vector3 CameraOffset { get; }

    /// <summary>Uniform scale of the mesh root (clamped to a small minimum at spawn).</summary>
    public float Scale { get; }

    /// <summary>Optional pulsing point light at the mesh core; null for no light.</summary>
    public HeldLightSpec? Light { get; }

    public bool PreserveAuthoredOrigin { get; }
    public Vector3 RotationEuler { get; }
    public HeldMeshWorldSpec? World { get; }
    public Func<Player, HeldMeshPresentation>? PresentationFactory { get; }
}

/// <summary>A full-size observer model using the same mesh and authored grip as the first-person model.</summary>
public sealed class HeldMeshWorldSpec
{
    public HeldMeshWorldSpec(Vector3 bodyOffset, float scale = 1f, Vector3? rotationEuler = null,
        Func<Player, Pose?>? poseResolver = null)
    {
        BodyOffset = bodyOffset;
        Scale = scale;
        RotationEuler = rotationEuler ?? Vector3.zero;
        PoseResolver = poseResolver;
    }

    public Vector3 BodyOffset { get; }
    public float Scale { get; }
    public Vector3 RotationEuler { get; }
    /// <summary>Optional native attachment world pose. Null result uses the body-local fallback.</summary>
    public Func<Player, Pose?>? PoseResolver { get; }
}

/// <summary>Per-visual visibility ownership, supplied by the consuming plugin before any network spawn.</summary>
public sealed class HeldMeshPresentation
{
    public HeldMeshPresentation(bool showFirstPerson, Action<AdminToy, bool> beforeSpawn, Action? onDestroy = null,
        bool showWorld = true, Func<float>? worldAlpha = null)
    {
        ShowFirstPerson = showFirstPerson;
        BeforeSpawn = beforeSpawn;
        OnDestroy = onDestroy;
        ShowWorld = showWorld;
        _worldAlpha = worldAlpha;
    }

    public bool ShowFirstPerson { get; }
    public bool ShowWorld { get; }
    private readonly Func<float>? _worldAlpha;
    /// <summary>Observer opacity for bespoke animated primitives whose colour changes after spawning.</summary>
    public float WorldAlpha => _worldAlpha?.Invoke() ?? 1f;
    /// <summary>The boolean distinguishes the observer mesh from the owner's first-person mesh.</summary>
    public Action<AdminToy, bool> BeforeSpawn { get; }
    public Action? OnDestroy { get; }
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
