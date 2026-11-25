using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Resolves feet and prop contacts after motion, before effects consume render depth.</summary>
[DefaultExecutionOrder(8000)]
internal sealed class WorldDepthOrder : MonoBehaviour
{
    internal const int LayerId = 0; // Default is the shared layer for interleaved world art.
    internal static readonly int GroundEffectsLayerId = SortingLayer.NameToID("Ground Effects");
    private const int FirstOrder = -32760;
    private const int ReservedSlots = 4;
    private static readonly List<YSortableObject> Participants = new List<YSortableObject>();
    private static readonly System.Comparison<YSortableObject> CompareDepth = Compare;
    private static WorldDepthOrder driver;
    private static bool membershipChanged;
    private static bool warnedAboutCapacity;
    private static int slotStride = ReservedSlots;

    internal static IReadOnlyList<YSortableObject> Entries => Participants;
    internal static uint MembershipVersion { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Participants.Clear();
        driver = null;
        membershipChanged = false;
        warnedAboutCapacity = false;
        MembershipVersion = 0;
        slotStride = ReservedSlots;
    }

    internal static void Register(YSortableObject actor)
    {
        actor.ParticipantIndex = Participants.Count;
        Participants.Add(actor);
        MembershipChanged();
        if (driver != null || !Application.isPlaying) return;
        driver = new GameObject("World Depth Sort").AddComponent<WorldDepthOrder>();
        DontDestroyOnLoad(driver.gameObject);
    }

    internal static void Unregister(YSortableObject actor)
    {
        int index = actor.ParticipantIndex;
        if ((uint)index >= (uint)Participants.Count || !ReferenceEquals(Participants[index], actor)) return;
        int last = Participants.Count - 1;
        var tail = Participants[last];
        Participants[index] = tail;
        tail.ParticipantIndex = index;
        Participants.RemoveAt(last);
        actor.ParticipantIndex = -1;
        MembershipChanged();
    }

    private static void MembershipChanged()
    {
        membershipChanged = true;
        unchecked { MembershipVersion++; }
    }

    private void LateUpdate()
    {
        if (!UnityNote.SceneLoader.FloorIsCovered) Refresh();
    }

    internal static void Refresh()
    {
        // Sample all anchors since a static prop may have been moved by a containing room.
        bool needsSort = membershipChanged;
        for (int i = 0; i < Participants.Count; i++)
        {
            Participants[i].SampleDepth();
            if (!needsSort && i > 0)
                needsSort = Compare(Participants[i - 1], Participants[i]) > 0;
        }

        if (!needsSort) return;
        Participants.Sort(CompareDepth);
        membershipChanged = false;

        int count = Participants.Count;
        slotStride = Mathf.Clamp(-FirstOrder / Mathf.Max(1, count), 1, ReservedSlots);
        if (count > -FirstOrder && !warnedAboutCapacity)
        {
            warnedAboutCapacity = true;
            Debug.LogWarning("The world depth layer exceeded its available sorting orders.");
        }

        for (int i = 0; i < count; i++)
        {
            Participants[i].ParticipantIndex = i;
            Participants[i].ApplyWorldOrder(Mathf.Min(-1, FirstOrder + i * slotStride));
        }
    }

    private static int Compare(YSortableObject a, YSortableObject b)
    {
        int depth = b.Depth.CompareTo(a.Depth);
        if (depth != 0) return depth;
        int priority = a.TiePriority.CompareTo(b.TiePriority);
        return priority != 0 ? priority : a.StableId.CompareTo(b.StableId);
    }

    internal static SortingGroup FindEffectiveGroup(Renderer renderer)
    {
        SortingGroup group = null;
        for (var node = renderer.transform; node != null; node = node.parent)
        {
            if (!node.TryGetComponent<SortingGroup>(out var candidate) || !candidate.isActiveAndEnabled) continue;
            group = candidate;
            if (candidate.sortAtRoot) break;
        }
        return group;
    }

    internal static void ReadOrder(Renderer renderer, SortingGroup group, out int layerId, out int order)
    {
        if (group != null && group.isActiveAndEnabled)
        {
            layerId = group.sortingLayerID;
            order = group.sortingOrder;
        }
        else
        {
            layerId = renderer.sortingLayerID;
            order = renderer.sortingOrder;
        }
    }

    internal static YSortableObject FindParticipant(Transform target)
    {
        if (target == null) return null;
        var sort = target.GetComponentInParent<YSortableObject>();
        return sort != null ? sort : target.GetComponentInChildren<YSortableObject>();
    }

    internal static int AttachmentOrder(int layerId, int ownerOrder, int offset)
        => ownerOrder + (layerId == LayerId && ownerOrder < 0 ? Mathf.Min(offset, slotStride - 1) : offset);

    internal static SortingGroup BindEffect(GameObject effect, SortingGroup group, YSortableObject target)
    {
        if (target == null || target.Renderer == null) return group;
        if (group == null) group = effect.AddComponent<SortingGroup>();
        SyncEffect(group, target);
        group.enabled = true;
        return group;
    }

    internal static void SyncEffect(SortingGroup effect, YSortableObject target)
    {
        if (effect == null || target == null || !target.isActiveAndEnabled || target.Renderer == null) return;
        ReadOrder(target.Renderer, target.Group, out int layer, out int order);
        int effectOrder = AttachmentOrder(layer, order, 1);
        if (effect.sortingLayerID != layer) effect.sortingLayerID = layer;
        if (effect.sortingOrder != effectOrder) effect.sortingOrder = effectOrder;
    }
}
