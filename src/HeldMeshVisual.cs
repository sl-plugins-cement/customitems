using System;
using System.Collections.Generic;
using InventorySystem.Items;
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
    private PrimitiveObjectToy? _worldRoot;
    private HeldMeshPresentation? _presentation;
    private PlayerModelAttachment? _attachment;
    private LightSourceToy? _light;
    private CoroutineHandle _animate;
    private bool _destroyed;
    private ItemBase? _carrier;
    private bool _observingCarrier;

    public HeldMeshVisual(Player player, HeldMeshSpec spec)
    {
        _player = player;
        _spec = spec;
    }

    public bool IsDestroyed => _destroyed || (_attachment != null && _attachment.IsDestroyed) ||
        ((_root == null || _root.IsDestroyed) && (_worldRoot == null || _worldRoot.IsDestroyed));

    /// <summary>Raised when the exact native carrier bound to a canonical visual is removed.
    /// Owners may clear armed/channel state; the visual always destroys itself afterward.</summary>
    public event Action? CarrierRemoved;

    /// <summary>Refreshes a canonical visual's exact native carrier before force-deselecting it.
    /// A null selection retains the existing binding; legacy noncanonical visuals are unchanged.</summary>
    public void BindCurrentItem()
    {
        if (_destroyed || !_spec.PreserveAuthoredOrigin || _player.IsDestroyed) return;
        ItemBase? current = _player.CurrentItem?.Base;
        if (current == null) return;
        _carrier = current;
        if (_observingCarrier) return;
        ItemBase.OnItemRemoved += OnCarrierRemoved;
        _observingCarrier = true;
    }

    private void OnCarrierRemoved(ItemBase item)
    {
        if (_destroyed || !ReferenceEquals(_carrier, item)) return;
        try { CarrierRemoved?.Invoke(); }
        catch (Exception exception)
        { Logger.Warn($"[CustomItems:HeldMesh] Carrier cleanup failed: {exception.GetBaseException().Message}"); }
        finally { Destroy(); }
    }

    /// <summary>Spawns the root, mesh children, and optional light, then starts the per-frame tracking coroutine.</summary>
    public bool Spawn()
    {
        if (_destroyed || _player == null || _player.IsDestroyed || _player.ReferenceHub == null)
            return false;
        if (_spec.PreserveAuthoredOrigin)
        {
            bool hasGeometry = false;
            foreach (MeshPrimitive primitive in _spec.Primitives)
                if (!primitive.IsMarker && (primitive.Flags & PrimitiveFlags.Visible) != 0)
                { hasGeometry = true; break; }
            if (!hasGeometry) return false;
        }
        try
        {
            // Native inventory clearing can remove a force-deselected item without another
            // ChangedItem event. ItemBase.OnItemRemoved also drives LabAPI's item-wrapper lifecycle.
            BindCurrentItem();
            _presentation = _spec.PresentationFactory?.Invoke(_player);
            if (_presentation != null && !_presentation.ShowFirstPerson && (!_presentation.ShowWorld || _spec.World == null))
            { Destroy(); return false; }
            if (_spec.PreserveAuthoredOrigin)
            {
                _attachment = new PlayerModelAttachment(_player);
                if (!_attachment.Spawn()) { Destroy(); return false; }
            }
            if (_presentation == null || _presentation.ShowFirstPerson)
                _root = SpawnRoot(isWorld: false);
            if (_spec.World != null && (_presentation == null || _presentation.ShowWorld))
                _worldRoot = SpawnRoot(isWorld: true);
            if (IsDestroyed)
            {
                Destroy();
                return false;
            }

            Vector3 meshCenter = _spec.PreserveAuthoredOrigin ? Vector3.zero : ComputeMeshCenter();
            if (_root != null) SpawnGeometry(_root.Transform, meshCenter, isWorld: false);
            if (_worldRoot != null) SpawnGeometry(_worldRoot.Transform, meshCenter, isWorld: true);
            if (_spec.Light != null && _root != null && !IsDestroyed)
                SpawnLight(_spec.Light, meshCenter);
            _animate = Timing.RunCoroutine(Animate());
            return true;
        }
        catch (Exception exception)
        {
            Logger.Warn($"[CustomItems:HeldMesh] Spawn failed: {exception.GetBaseException().Message}");
            Destroy();
            return false;
        }
    }

    private PrimitiveObjectToy SpawnRoot(bool isWorld)
    {
        TryComputeLocalPose(out Vector3 initialPos, out Quaternion initialRot);
        if (isWorld) TryComputeWorldLocalPose(out initialPos, out initialRot);
        PrimitiveObjectToy root = PrimitiveObjectToy.Create(
                initialPos,
                initialRot,
                Vector3.one * Mathf.Max(0.02f, isWorld ? _spec.World!.Scale : _spec.Scale),
                _attachment?.Transform ?? _player.ReferenceHub.transform,
                networkSpawn: false);
        _toys.Add(root);
        root.Type = PrimitiveType.Cube;
        root.Flags = PrimitiveFlags.None;
        root.Color = new Color(0f, 0f, 0f, 0f);
        root.IsStatic = false;
        root.Base.NetworkMovementSmoothing = RawMovementSmoothing;
        root.SyncInterval = isWorld ? 0.05f : 0f;
        _presentation?.BeforeSpawn(root, isWorld);
        root.Spawn();
        return root;
    }

    private void SpawnGeometry(Transform root, Vector3 meshCenter, bool isWorld)
    {
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

            SpawnMeshPrimitive(primitive, primitive.Position - meshCenter, root, spawnedByName, isWorld);
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
                SpawnMeshPrimitive(child, child.Position, parent, spawnedByName, isWorld);
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

    }

    /// <summary>Spawns one mesh primitive under <paramref name="parent"/> and records it by name so later
    /// primitives can parent to it. Children stay static: only the single root replicates movement.</summary>
    private void SpawnMeshPrimitive(
        MeshPrimitive primitive,
        Vector3 localPosition,
        Transform parent,
        Dictionary<string, Transform> spawnedByName,
        bool isWorld)
    {
        try
        {
            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(
                localPosition,
                Quaternion.Euler(primitive.Rotation),
                primitive.Scale,
                parent,
                networkSpawn: false);
            _toys.Add(toy);
            toy.Type = primitive.Type;
            // Authored item presentations must never introduce gameplay collision.
            toy.Flags = _spec.PreserveAuthoredOrigin ? primitive.Flags & ~PrimitiveFlags.Collidable : primitive.Flags;
            toy.Color = primitive.Color;
            toy.IsStatic = true;
            _presentation?.BeforeSpawn(toy, isWorld);
            toy.Spawn();
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
            _toys.Add(_light);
            _light.Type = LightType.Point;
            _light.Intensity = lightSpec.BaseIntensity;
            _light.Range = lightSpec.Range;
            _light.Color = lightSpec.Color;
            _light.ShadowType = LightShadows.None;
            _light.IsStatic = false;
            _light.Base.NetworkMovementSmoothing = RawMovementSmoothing;
            _light.SyncInterval = 0f;
            _presentation?.BeforeSpawn(_light, false);
            _light.Spawn();
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
            _attachment?.UpdateScale();
            if (_attachment != null && _attachment.IsDestroyed)
            { Destroy(); yield break; }
            if (_player.IsDestroyed || (_spec.PreserveAuthoredOrigin && !_player.IsAlive))
            {
                Destroy();
                yield break;
            }
            if (_root != null && !_root.IsDestroyed && TryComputeLocalPose(out Vector3 localPos, out Quaternion localRot))
            {
                _root.Transform.SetLocalPositionAndRotation(localPos, localRot);
            }

            if (_worldRoot != null && !_worldRoot.IsDestroyed && TryComputeWorldLocalPose(out Vector3 worldPos, out Quaternion worldRot))
                _worldRoot.Transform.SetLocalPositionAndRotation(worldPos, worldRot);

            if (lightSpec != null && _light != null && !_light.IsDestroyed)
            {
                float t = (Time.timeSinceLevelLoad - startAt) * 2f * Mathf.PI * Mathf.Max(0.05f, lightSpec.PulseHz);
                _light.Intensity = lightSpec.BaseIntensity + (lightSpec.PulseAmplitude * Mathf.Sin(t));
            }

            yield return Timing.WaitForOneFrame;
        }
        Destroy();
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
        Transform? body = _attachment?.Transform ?? (_player.ReferenceHub != null ? _player.ReferenceHub.transform : null);
        if (camera == null || body == null)
        {
            return false;
        }

        Vector3 worldPos = camera.position + (camera.rotation * _spec.CameraOffset);
        localPos = body.InverseTransformPoint(worldPos);
        localRot = Quaternion.Inverse(body.rotation) * camera.rotation * Quaternion.Euler(_spec.RotationEuler);
        return true;
    }

    private bool TryComputeWorldLocalPose(out Vector3 localPos, out Quaternion localRot)
    {
        HeldMeshWorldSpec? world = _spec.World;
        localPos = world?.BodyOffset ?? Vector3.zero;
        localRot = Quaternion.Euler(world?.RotationEuler ?? Vector3.zero);
        if (world == null || _player.IsDestroyed || _player.ReferenceHub == null) return false;
        Pose? pose = world.PoseResolver?.Invoke(_player);
        if (pose.HasValue)
        {
            Transform body = _attachment?.Transform ?? _player.ReferenceHub.transform;
            localPos = body.InverseTransformPoint(pose.Value.position);
            localRot = Quaternion.Inverse(body.rotation) * pose.Value.rotation * Quaternion.Euler(world.RotationEuler);
        }
        return true;
    }

    public void Destroy()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        if (_observingCarrier)
        {
            ItemBase.OnItemRemoved -= OnCarrierRemoved;
            _observingCarrier = false;
        }
        _carrier = null;
        CarrierRemoved = null;
        if (_animate.IsRunning)
        {
            Timing.KillCoroutines(_animate);
        }

        // Children must disappear before their network parents.
        for (int i = _toys.Count - 1; i >= 0; i--)
        {
            AdminToy toy = _toys[i];
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
        _worldRoot = null;
        _light = null;
        _attachment?.Destroy();
        _attachment = null;
        try { _presentation?.OnDestroy?.Invoke(); }
        catch (Exception exception)
        { Logger.Warn($"[CustomItems:HeldMesh] Presentation cleanup failed: {exception.GetBaseException().Message}"); }
        _presentation = null;
    }
}
