using UnityEngine;

/// <summary>
/// 플레이어가 벽 등 가리는 오브젝트 뒤로 들어가 화면에서 안 보이게 될 때,
/// 별도의 "실루엣" 스프라이트를 가리는 오브젝트보다 항상 위에 그려서
/// 플레이어 위치를 계속 식별할 수 있게 해줍니다.
///
/// 사용법:
/// 1. 플레이어 오브젝트의 자식으로 빈 GameObject(예: "Silhouette")를 만들고 SpriteRenderer를 추가
/// 2. 그 SpriteRenderer를 silhouetteRenderer 필드에 연결
/// 3. silhouetteRenderer의 sortingOrder는 무조건 크게(예: 9000) 고정해서 항상 다른 오브젝트보다 위에 그려지게 함
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerOcclusionSilhouette : MonoBehaviour
{
    [Header("실루엣 렌더러")]
    [Tooltip("플레이어 본체 스프라이트를 따라 그릴 별도의 SpriteRenderer (자식 오브젝트에 둘 것). sortingOrder는 크게 고정해서 항상 위에 그려지게 설정.")]
    [SerializeField] private SpriteRenderer silhouetteRenderer;

    [Tooltip("실루엣 색상/투명도. 알파가 너무 높으면 본체처럼 또렷하게 보여서 가리는 의미가 없어지고, 너무 낮으면 안 보임.")]
    [SerializeField] private Color silhouetteColor = new Color(1f, 1f, 1f, 0.55f);

    [Tooltip("Occlusion check radius. Objects farther than this skip the bounds check entirely (perf).")]
    [SerializeField] private float maxCheckDistance = 3f;

    
    private SpriteRenderer _mainRenderer;

    
private void Awake()
    {
        _mainRenderer = GetComponent<SpriteRenderer>();

        if (silhouetteRenderer != null)
        {
            silhouetteRenderer.color = silhouetteColor;
            silhouetteRenderer.enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (silhouetteRenderer == null || _mainRenderer == null) return;

        // 본체 애니메이션을 그대로 따라가도록 매 프레임 스프라이트 동기화
        silhouetteRenderer.sprite = _mainRenderer.sprite;
        silhouetteRenderer.flipX = _mainRenderer.flipX;
        silhouetteRenderer.flipY = _mainRenderer.flipY;

        silhouetteRenderer.enabled = IsOccluded();
    }

    /// <summary>
    /// 주변에 플레이어보다 sortingOrder가 높은(=화면상 앞에 그려지는) SpriteRenderer를 가진
    /// 오브젝트가 겹쳐 있으면 "가려진 상태"로 판단합니다.
    /// </summary>
private bool IsOccluded()
    {
        if (_mainRenderer == null) return false;
        Bounds myBounds = _mainRenderer.bounds;
        float maxDistSqr = maxCheckDistance * maxCheckDistance;

        var instances = YSortableObject.ActiveInstances;
        for (int i = 0; i < instances.Count; i++)
        {
            var ys = instances[i];
            if (ys == null) continue;

            var occluderRenderer = ys.Renderer;
            if (occluderRenderer == null || occluderRenderer.transform == transform) continue;

            // Cheap distance check first - skip far-away objects before touching bounds (perf)
            float distSqr = (occluderRenderer.transform.position - transform.position).sqrMagnitude;
            if (distSqr > maxDistSqr) continue;

            if (occluderRenderer.sortingLayerID != _mainRenderer.sortingLayerID) continue;
            if (occluderRenderer.sortingOrder <= _mainRenderer.sortingOrder) continue;

            // Actual visual overlap check using rendered sprite bounds
            if (occluderRenderer.bounds.Intersects(myBounds)) return true;
        }

        return false;
    }
}
