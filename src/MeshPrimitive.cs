using UnityEngine;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace CustomItems;

/// <summary>
/// A neutral, framework-agnostic description of one primitive in a held custom mesh, so the shared
/// <see cref="HeldMeshVisual"/> never depends on any plugin's own model type (e.g. reinforcements'
/// <c>LogoPrimitive</c>). Callers convert their loaded <c>.mer</c> primitives into these. Positions are in
/// mesh-local space; <see cref="HeldMeshVisual"/> centres the mesh on its bounding box automatically.
///
/// Primitives whose <see cref="Name"/> starts with <c>"marker_"</c> are treated as invisible anchors (the
/// <c>.mer</c> convention) and are not rendered; a marker can still position the core light via
/// <see cref="HeldLightSpec.AnchorMarker"/>.
/// </summary>
public readonly struct MeshPrimitive
{
    public MeshPrimitive(string name, Vector3 position, Vector3 rotation, Vector3 scale, PrimitiveType type, Color color, PrimitiveFlags flags)
        : this(name, position, rotation, scale, type, color, flags, parentName: null)
    {
    }

    /// <summary>
    /// Overload carrying an explicit <paramref name="parentName"/> so a mesh can contain SHEAR RIGS: an
    /// exact parallelogram needs a non-uniformly scaled parent plus a rotated child, which no single
    /// AdminToy can express (pos+rot+scale = R*S, never shear). The 7-argument constructor is kept
    /// byte-compatible for callers built against the earlier assembly.
    /// </summary>
    public MeshPrimitive(string name, Vector3 position, Vector3 rotation, Vector3 scale, PrimitiveType type, Color color, PrimitiveFlags flags, string? parentName)
    {
        Name = name;
        Position = position;
        Rotation = rotation;
        Scale = scale;
        Type = type;
        Color = color;
        Flags = flags;
        ParentName = parentName;
    }

    public string Name { get; }

    public Vector3 Position { get; }

    public Vector3 Rotation { get; }

    public Vector3 Scale { get; }

    public PrimitiveType Type { get; }

    public Color Color { get; }

    /// <summary>The primitive's flags (the caller decides visibility; <c>None</c> renders invisible).</summary>
    public PrimitiveFlags Flags { get; }

    /// <summary>
    /// <see cref="Name"/> of another primitive in the same mesh that this one is a CHILD of, or null for
    /// the normal case (a direct child of the mesh root). When set, <see cref="Position"/>,
    /// <see cref="Rotation"/> and <see cref="Scale"/> are local to that parent and are NOT re-centred on
    /// the mesh bounding box. Used for shear rigs; the parent is typically an invisible
    /// (<see cref="PrimitiveFlags.None"/>) non-uniformly scaled primitive.
    /// </summary>
    public string? ParentName { get; }

    internal bool IsMarker => Name != null && Name.StartsWith("marker_", System.StringComparison.OrdinalIgnoreCase);
}
