using System.Collections.Generic;
using UnityEngine;

// Tracks a visible piece whose height and ground contact belong to different transforms.
[RequireComponent(typeof(SpriteRenderer)), DisallowMultipleComponent]
public sealed class AirborneShadow : MonoBehaviour
{
    internal static readonly List<AirborneShadow> Active = new();
    internal SpriteRenderer Picture { get; private set; }
    private YSortableObject owner;
    private Vector2 floor;
    internal Vector2 Floor => owner != null ? (Vector2)owner.ShadowGroundPosition : floor;
    internal bool IsHand => owner != null;

    internal static void AttachHand(SpriteRenderer picture, YSortableObject body)
    {
        if (body != null) picture.gameObject.AddComponent<AirborneShadow>().owner = body;
    }

    internal void SetFloor(Vector2 value) => floor = value;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetParts() => Active.Clear();

    private void Awake() => Picture = GetComponent<SpriteRenderer>();
    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
}
