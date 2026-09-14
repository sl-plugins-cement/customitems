using System;
using LabApi.Features.Wrappers;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace CustomItems;

/// <summary>
/// An invisible, globally replicated player attachment that cancels the player's local scale.
/// Parent rotated model-pose roots beneath this neutral carrier to preserve authored metres
/// even when the player has a nonuniform scale. Visibility scopes belong to its descendants.
/// </summary>
/// <remarks>
/// This object owns no update loop. Its owner calls <see cref="UpdateScale"/> from its existing
/// presentation tick and destroys descendant toys before calling <see cref="Destroy"/>.
/// Compensation applies to the ReferenceHub's local scale, not arbitrary scaled ancestors.
/// </remarks>
public sealed class PlayerModelAttachment
{
    private const float MinimumScaleMagnitude = 0.0001f;
    private const byte RawMovementSmoothing = 60;

    private readonly Player _player;
    private PrimitiveObjectToy? _root;
    private Vector3 _inverseScale;
    private bool _destroyed;

    public PlayerModelAttachment(Player player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>The compensated carrier. Available only after a successful <see cref="Spawn"/>.</summary>
    public Transform Transform => !IsDestroyed
        ? _root!.Transform
        : throw new InvalidOperationException("The player model attachment is not spawned.");

    public bool IsDestroyed => _destroyed || _root == null || _root.IsDestroyed;

    /// <summary>
    /// Spawns an unscoped invisible carrier. Returns false if the player has no invertible,
    /// finite local scale; zero scale cannot be compensated by a finite transform.
    /// </summary>
    public bool Spawn()
    {
        if (_destroyed || !TryGetInverseScale(out Vector3 inverseScale))
            return false;
        if (_root != null && !_root.IsDestroyed)
        {
            UpdateScale();
            return !IsDestroyed;
        }

        try
        {
            _root = PrimitiveObjectToy.Create(
                Vector3.zero,
                Quaternion.identity,
                inverseScale,
                _player.ReferenceHub.transform,
                networkSpawn: false);
            _root.Type = PrimitiveType.Cube;
            _root.Flags = PrimitiveFlags.None;
            _root.Color = Color.clear;
            _root.IsStatic = false;
            _root.Base.NetworkMovementSmoothing = RawMovementSmoothing;
            _root.SyncInterval = 0.05f;
            _root.Spawn();
            _inverseScale = inverseScale;
            return true;
        }
        catch (Exception exception)
        {
            Logger.Warn($"[CustomItems:PlayerModelAttachment] Spawn failed: {exception.GetBaseException().Message}");
            Destroy();
            return false;
        }
    }

    /// <summary>
    /// Writes the inverse scale only when it changes. A destroyed player or invalid scale
    /// destroys the carrier; the owner should observe <see cref="IsDestroyed"/> and clean up.
    /// </summary>
    public void UpdateScale()
    {
        if (IsDestroyed)
            return;
        if (!TryGetInverseScale(out Vector3 inverseScale))
        {
            Destroy();
            return;
        }
        if (_inverseScale.Equals(inverseScale))
            return;

        _root!.Scale = inverseScale;
        _inverseScale = inverseScale;
    }

    private bool TryGetInverseScale(out Vector3 inverseScale)
    {
        inverseScale = Vector3.one;
        if (_player.IsDestroyed || _player.ReferenceHub == null)
            return false;

        Vector3 scale = _player.ReferenceHub.transform.localScale;
        if (!IsInvertibleComponent(scale.x) || !IsInvertibleComponent(scale.y) || !IsInvertibleComponent(scale.z))
            return false;

        inverseScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        return true;
    }

    private static bool IsInvertibleComponent(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) >= MinimumScaleMagnitude;

    /// <summary>Idempotently destroys the owned carrier. Destroy descendant toys first.</summary>
    public void Destroy()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        PrimitiveObjectToy? root = _root;
        _root = null;
        try
        {
            if (root != null && !root.IsDestroyed)
                root.Destroy();
        }
        catch (Exception exception)
        {
            Logger.Warn($"[CustomItems:PlayerModelAttachment] Destroy failed: {exception.GetBaseException().Message}");
        }
    }
}
