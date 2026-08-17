using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace CustomItems;

/// <summary>
/// A camera-tracked first-person mesh: an invisible <see cref="PrimitiveObjectToy"/> root parented to the
/// holder's body, re-posed every frame from a camera-relative offset so static child primitives present as a
/// pitch-tracked, screen-fixed viewmodel. Only the single non-static root replicates movement; the children
/// follow via parenting. This is the pattern duplicated verbatim by reinforcements' SRA <c>HeldAnchorVisual</c>
/// and Medic <c>HeldHealVisual</c>; it lives here once. Manage its lifecycle through <see cref="HeldMeshManager"/>.
/// </summary>
public sealed class HeldMeshVisual
{
    private const byte RawMovementSmoothing = 60;

    private readonly Player _player;
    private readonly HeldMeshSpec _spec;
    private readonly List<AdminToy> _toys = new();
    private PrimitiveObjectToy? _root;
    private LightSourceToy? _light;
    private CoroutineHandle _animate;
    private bool _destroyed;

    public HeldMeshVisual(Player player, HeldMeshSpec spec)
    {
        _player = player;
        _spec = spec;
    }

    public bool IsDestroyed => _destroyed || _root == null || _root.IsDestroyed;

    /// <summary>Spawns the root, mesh children, and optional light, then starts the per-frame tracking coroutine.</summary>
    public bool Spawn()
    {
        TryComputeLocalPose(out Vector3 initialPos, out Quaternion initialRot);
        try
        {
            _root = PrimitiveObjectToy.Create(
                initialPos,
                initialRot,
                Vector3.one * Mathf.Max(0.02f, _spec.Scale),
                _player.ReferenceHub.transform,
                networkSpawn: false);
            _root.Type = PrimitiveType.Cube;
            _root.Flags = PrimitiveFlags.None;
            _root.Color = new Color(0f, 0f, 0f, 0f);
            _root.IsStatic = false;
            _root.Base.NetworkMovementSmoothing = RawMovementSmoothing;
            _root.SyncInterval = 0f;
            _root.Spawn();
            _toys.Add(_root);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[CustomItems:HeldMesh] Root failed: {exception.GetBaseException().Message}");
            return false;
        }

        // Centre the mesh on its bounding box so the camera offset positions the device's middle.
        Vector3 meshCenter = ComputeMeshCenter();

        // Two passes so shear rigs work: a primitive naming a ParentName is spawned UNDER that
        // primitive's transform, with its authored local pose (no mesh-centre offset — that only
        // applies to mesh-root children). Pending children are re-swept until a pass resolves nothing,
        // which supports arbitrary nesting depth and simply drops an unresolvable parent reference.
        Dictionary<string, Transform> spawnedByName = new(StringComparer.OrdinalIgnoreCase);
        List<MeshPrimitive> pending = new();
        foreach (MeshPrimitive primitive in _spec.Primitives)
        {
            if (IsDestroyed)
            {
                break;
            }

            if (primitive.IsMarker)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(primitive.ParentName))
            {
                pending.Add(primitive);
                continue;
            }

            SpawnMeshPrimitive(primitive, primitive.Position - meshCenter, _root!.Transform, spawnedByName);
        }

        while (pending.Count > 0 && !IsDestroyed)
        {
            int before = pending.Count;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                MeshPrimitive child = pending[i];
                if (!spawnedByName.TryGetValue(child.ParentName!, out Transform parent))
                {
                    continue;
                }

                pending.RemoveAt(i);
                SpawnMeshPrimitive(child, child.Position, parent, spawnedByName);
            }

            if (pending.Count == before)
            {
                foreach (MeshPrimitive orphan in pending)
                {
                    Logger.Warn($"[CustomItems:HeldMesh] '{orphan.Name}' names missing parent '{orphan.ParentName}'; skipped.");
                }

                break;
            }
        }

        if (_spec.Light != null && !IsDestroyed)
        {
            SpawnLight(_spec.Light, meshCenter);
        }

        _animate = Timing.RunCoroutine(Animate());
        return true;
    }

    /// <summary>Spawns one mesh primitive under <paramref name="parent"/> and records it by name so later
    /// primitives can parent to it. Children stay static: only the single root replicates movement.</summary>
    private void SpawnMeshPrimitive(
        MeshPrimitive primitive,
        Vector3 localPosition,
        Transform parent,
        Dictionary<string, Transform> spawnedByName)
    {
        try
        {
            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(
                localPosition,
                Quaternion.Euler(primitive.Rotation),
                primitive.Scale,
                parent,
                networkSpawn: false);
            toy.Type = primitive.Type;
            toy.Flags = primitive.Flags;
            toy.Color = primitive.Color;
            toy.IsStatic = true;
            toy.Spawn();
            _toys.Add(toy);
            if (!string.IsNullOrEmpty(primitive.Name))
            {
                spawnedByName[primitive.Name] = toy.Transform;
            }
        }
        catch (Exception exception)
        {
            Logger.Warn($"[CustomItems:HeldMesh] Primitive failed: {exception.GetBaseException().Message}");
        }
    }

    private void SpawnLight(HeldLightSpec lightSpec, Vector3 meshCenter)
    {
        Vector3 localPosition = lightSpec.LocalPosition;
        if (!string.IsNullOrEmpty(lightSpec.AnchorMarker))
        {
            foreach (MeshPrimitive primitive in _spec.Primitives)
            {
                if (string.Equals(primitive.Name, lightSpec.AnchorMarker, StringComparison.OrdinalIgnoreCase))
                {
                    localPosition = primitive.Position - meshCenter;
                    break;
                }
            }
        }

        try
        {
            _light = LightSourceToy.Create(localPosition, Quaternion.identity, _root!.Transform, networkSpawn: false);
            _light.Type = LightType.Point;
            _light.Intensity = lightSpec.BaseIntensity;
            _light.Range = lightSpec.Range;
            _light.Color = lightSpec.Color;
            _light.ShadowType = LightShadows.None;
            _light.IsStatic = false;
            _light.Base.NetworkMovementSmoothing = RawMovementSmoothing;
            _light.SyncInterval = 0f;
            _light.Spawn();
            _toys.Add(_light);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[CustomItems:HeldMesh] Light failed: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>Axis-aligned centre of the visible mesh (markers excluded), in mesh-local space.</summary>
    private Vector3 ComputeMeshCenter()
    {
        bool any = false;
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        foreach (MeshPrimitive primitive in _spec.Primitives)
        {
            // Parented primitives are authored in their parent's frame, so their coordinates say
            // nothing about mesh-space extent — the parent they sit inside already contributes it.
            if (primitive.IsMarker || !string.IsNullOrEmpty(primitive.ParentName))
            {
                continue;
            }

            Vector3 half = new(
                Mathf.Abs(primitive.Scale.x) * 0.5f,
                Mathf.Abs(primitive.Scale.y) * 0.5f,
                Mathf.Abs(primitive.Scale.z) * 0.5f);
            min = Vector3.Min(min, primitive.Position - half);
            max = Vector3.Max(max, primitive.Position + half);
            any = true;
        }

        return any ? (min + max) * 0.5f : Vector3.zero;
    }

    /// <summary>Per-frame: re-anchor the root to the camera-relative viewmodel pose and pulse the core light.</summary>
    private IEnumerator<float> Animate()
    {
        HeldLightSpec? lightSpec = _spec.Light;
        float startAt = Time.timeSinceLevelLoad;
        while (!IsDestroyed)
        {
            if (_root != null && !_root.IsDestroyed && TryComputeLocalPose(out Vector3 localPos, out Quaternion localRot))
            {
                _root.Transform.SetLocalPositionAndRotation(localPos, localRot);
            }

            if (lightSpec != null && _light != null && !_light.IsDestroyed)
            {
                float t = (Time.timeSinceLevelLoad - startAt) * 2f * Mathf.PI * Mathf.Max(0.05f, lightSpec.PulseHz);
                _light.Intensity = lightSpec.BaseIntensity + (lightSpec.PulseAmplitude * Mathf.Sin(t));
            }

            yield return Timing.WaitForOneFrame;
        }
    }

    /// <summary>
    /// Body-local pose that places the mesh at the camera-space <see cref="HeldMeshSpec.CameraOffset"/>.
    /// Only the player root replicates a parent, so the camera-relative world pose is converted into the
    /// body frame to present a pitch-tracked, screen-fixed viewmodel.
    /// </summary>
    private bool TryComputeLocalPose(out Vector3 localPos, out Quaternion localRot)
    {
        localPos = Vector3.zero;
        localRot = Quaternion.identity;
        if (_player == null || _player.IsDestroyed)
        {
            return false;
        }

        Transform? camera = _player.Camera;
        Transform? body = _player.ReferenceHub != null ? _player.ReferenceHub.transform : null;
        if (camera == null || body == null)
        {
            return false;
        }

        Vector3 worldPos = camera.position + (camera.rotation * _spec.CameraOffset);
        localPos = body.InverseTransformPoint(worldPos);
        localRot = Quaternion.Inverse(body.rotation) * camera.rotation;
        return true;
    }

    public void Destroy()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        if (_animate.IsRunning)
        {
            Timing.KillCoroutines(_animate);
        }

        foreach (AdminToy toy in _toys)
        {
            try
            {
                if (toy != null && !toy.IsDestroyed)
                {
                    toy.Destroy();
                }
            }
            catch (Exception exception)
            {
                Logger.Warn($"[CustomItems:HeldMesh] Destroy failed: {exception.GetBaseException().Message}");
            }
        }

        _toys.Clear();
        _root = null;
        _light = null;
    }
}
