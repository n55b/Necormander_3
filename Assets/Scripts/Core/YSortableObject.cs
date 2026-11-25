using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Registers one ground contact and one render unit with the world depth pass.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
public sealed class YSortableObject : MonoBehaviour
{
    [Tooltip("지면 접점. 비워 두면 오브젝트의 피벗을 사용합니다.")]
    [SerializeField] private Transform pivotOverride;
    [Tooltip("기준 Transform의 로컬 좌표에서 지면 접점을 보정합니다.")]
    [SerializeField] private Vector2 localAnchorOffset;
    [Tooltip("지면 높이가 같은 경우에만 적용합니다. 큰 값이 앞입니다.")]
    [SerializeField] private int tiePriority;
    [Tooltip("여러 그림을 함께 정렬할 때 그룹을 지정합니다.")]
    [SerializeField] private SortingGroup sortingGroup;
    [Tooltip("몸통이 가려졌을 때 플레이어 위치에 안개가 펼쳐지는 사물입니다.")]
    [SerializeField] private bool occludesPlayer;

    [SerializeField] private Transform shadowContact;
    public bool CastsGroundShadow { get; set; } = true;
    public Vector3 ShadowGroundPosition => shadowContact != null
        ? shadowContact.position : SpriteGroundContacts.Resolve(Renderer, GroundPosition);

    internal TreeCanopyWind Canopy { get; private set; }
    public SpriteRenderer Renderer { get; private set; }
    public SortingGroup Group => sortingGroup != null && sortingGroup.isActiveAndEnabled ? sortingGroup : null;
    public bool OccludesPlayer => occludesPlayer;
    public Vector3 GroundPosition
    {
        get
        {
            var pivot = pivotOverride != null ? pivotOverride : transform;
            return localAnchorOffset == Vector2.zero ? pivot.position : pivot.TransformPoint(localAnchorOffset);
        }
    }

    internal double Depth { get; private set; }
    internal Vector2 SampledGround { get; private set; }
    internal bool HasManualShadowContact => shadowContact != null;
    internal int TiePriority => tiePriority;
    internal int StableId { get; private set; }

    private void Awake()
    {
        Renderer = GetComponent<SpriteRenderer>();
        Canopy = GetComponent<TreeCanopyWind>();
        StableId = GetInstanceID();
    }

    internal int ParticipantIndex { get; set; } = -1;
    private void OnEnable() => WorldDepthOrder.Register(this);
    private void OnDisable() => WorldDepthOrder.Unregister(this);

    internal void SampleDepth()
    {
        // One logical pixel is 32 steps on the default lens.
        var ground = GroundPosition;
        SampledGround = ground;
        Depth = System.Math.Round(ground.y * 1024.0);
    }

    internal void ApplyWorldOrder(int order)
    {
        var group = Group;
        if (group != null)
        {
            if (!group.sortAtRoot) group.sortAtRoot = true;
            if (group.sortingLayerID != WorldDepthOrder.LayerId) group.sortingLayerID = WorldDepthOrder.LayerId;
            if (group.sortingOrder != order) group.sortingOrder = order;
        }
        else if (Renderer != null)
        {
            if (Renderer.sortingLayerID != WorldDepthOrder.LayerId) Renderer.sortingLayerID = WorldDepthOrder.LayerId;
            if (Renderer.sortingOrder != order) Renderer.sortingOrder = order;
        }
    }

    public void SetPivot(Transform pivot) => pivotOverride = pivot;

    public void SetGroundPoint(Vector3 worldPoint, int equalDepthPriority = 0)
    {
        pivotOverride = null;
        localAnchorOffset = transform.InverseTransformPoint(worldPoint);
        tiePriority = equalDepthPriority;
    }

    private void OnDrawGizmosSelected()
    {
        var point = GroundPosition;
        Gizmos.color = new Color(0.3f, 0.9f, 0.85f);
        Gizmos.DrawLine(point + Vector3.left * .24f, point + Vector3.right * .24f);
        Gizmos.DrawWireSphere(point, .055f);
    }
}
