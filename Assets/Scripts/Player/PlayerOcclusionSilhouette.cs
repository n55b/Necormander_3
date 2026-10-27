using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 플레이어가 벽 등 가리는 오브젝트 뒤로 들어가 화면에서 안 보이게 될 때,
/// 별도 실루엣을 가림막의 실제 불투명 픽셀과 겹친 부분에만 그립니다.
///
/// 사용법:
/// 1. 플레이어 오브젝트의 자식으로 빈 GameObject(예: "Silhouette")를 만들고 SpriteRenderer를 추가
/// 2. 그 SpriteRenderer를 silhouetteRenderer 필드에 연결
/// 3. silhouetteRenderer의 sortingOrder는 무조건 크게(예: 9000) 고정해서 항상 다른 오브젝트보다 위에 그려지게 함
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[DefaultExecutionOrder(9000)] // Y정렬과 본체 애니메이션 갱신 후 가려진 픽셀을 계산한다.
public class PlayerOcclusionSilhouette : MonoBehaviour
{
    [Header("실루엣 렌더러")]
    [Tooltip("플레이어 본체 스프라이트를 따라 그릴 별도의 SpriteRenderer (자식 오브젝트에 둘 것). sortingOrder는 크게 고정해서 항상 위에 그려지게 설정.")]
    [SerializeField] private SpriteRenderer silhouetteRenderer;

    [Tooltip("실루엣 색상/투명도. 알파가 너무 높으면 본체처럼 또렷하게 보여서 가리는 의미가 없어지고, 너무 낮으면 안 보임.")]
    [SerializeField] private Color silhouetteColor = new Color(.65f, .67f, .68f, .55f);

    [Tooltip("가림막을 확인할 최대 거리(월드 유닛). 이보다 먼 오브젝트는 검사하지 않습니다. 늘리면 멀리 있는 가림막도 찾지만 검사량이 늘어납니다.")]
    [SerializeField] private float maxCheckDistance = 3f;

    [Tooltip("실루엣이 반응할 대상의 레이어. 기본값은 Wall/Obstacle만 포함되어 몬스터(Enemy)는 가림막으로 취급하지 않습니다. 인스펙터에서 원하는 레이어로 바꿀 수 있습니다.")]
    [SerializeField] private LayerMask occluderLayers = 0;

    [Tooltip("위 occluderLayers가 설정 안 된 채(Nothing/0) 남아있으면 Awake에서 Wall+Obstacle로 자동 대체됩니다.")]
    [SerializeField] private bool autoDefaultToWallLayers = true;
    [Min(.01f), SerializeField] private float transitionDuration = .12f;
    [SerializeField, Range(.01f, 1f), InspectorName("가림막 불투명도 기준")]
    [Tooltip("가림막 스프라이트에서 이 알파 이상인 픽셀에만 실루엣을 표시합니다. 투명 여백에는 표시하지 않습니다.")]
    private float maskAlphaCutoff = .1f;
    private float _visibility;
    private readonly List<SpriteMask> _masks = new List<SpriteMask>();


    
    private SpriteRenderer _mainRenderer;

    
private void Awake()
    {
        _mainRenderer = GetComponent<SpriteRenderer>();

        if (autoDefaultToWallLayers && occluderLayers.value == 0)
        {
            occluderLayers = Layers.WallMask;
        }

        if (silhouetteRenderer != null)
        {
            silhouetteRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            silhouetteRenderer.color = silhouetteColor;
            silhouetteRenderer.enabled = false;
        }
    }

private void LateUpdate()
    {
        if (silhouetteRenderer == null || _mainRenderer == null) return;
        if (!PolishFeedback.ExistingEnabled(ExistingFeedbackCue.Silhouette))
        { _visibility = 0f; silhouetteRenderer.enabled = false; HideMasks(0); return; }

        // Sync sprite/flip every frame to follow body animation
        silhouetteRenderer.sprite = _mainRenderer.sprite;
        silhouetteRenderer.flipX = _mainRenderer.flipX;
        silhouetteRenderer.flipY = _mainRenderer.flipY;

        // Re-apply tint every frame (not just once in Awake) - otherwise any other system
        // that touches this renderer's color (hit flash, status effect tint, etc.) permanently
        // overrides our silhouette tint and it never recovers.
        bool occluded = UpdateMasks();
        _visibility = Mathf.MoveTowards(_visibility, occluded ? 1f : 0f, Time.deltaTime / Mathf.Max(.01f, transitionDuration));
        var tint = silhouetteColor;
        tint.a *= _visibility;
        silhouetteRenderer.color = tint;
        silhouetteRenderer.enabled = _visibility > 0f;
    }

    /// <summary>
    /// 주변에 플레이어보다 sortingOrder가 높은(=화면상 앞에 그려지는) SpriteRenderer를 가진
    /// 오브젝트가 겹쳐 있으면 "가려진 상태"로 판단합니다.
    /// </summary>
private bool UpdateMasks()
    {
        if (!_mainRenderer.enabled || _mainRenderer.color.a <= .001f)
        { HideMasks(0); return false; }
        Bounds myBounds = _mainRenderer.bounds;
        float maxDistSqr = maxCheckDistance * maxCheckDistance;
        Transform myRoot = transform.root;

        var instances = YSortableObject.ActiveInstances;
        int used = 0;
        for (int i = 0; i < instances.Count; i++)
        {
            var ys = instances[i];
            if (ys == null) continue;

            var occluderRenderer = ys.Renderer;
            if (occluderRenderer == null || !occluderRenderer.enabled || occluderRenderer.sprite == null
                || occluderRenderer.color.a < maskAlphaCutoff) continue;

            // Skip anything that belongs to my own character (body/hands/etc share the same root) -
            // otherwise my own hand (drawn in front of my body) gets mistaken for something occluding me.
            if (occluderRenderer.transform.root == myRoot) continue;

            // Cheap distance check first - skip far-away objects before touching bounds (perf)
            float distSqr = (occluderRenderer.transform.position - transform.position).sqrMagnitude;
            if (distSqr > maxDistSqr) continue;

            int layer = SortingLayer.GetLayerValueFromID(occluderRenderer.sortingLayerID);
            int bodyLayer = SortingLayer.GetLayerValueFromID(_mainRenderer.sortingLayerID);
            if (layer < bodyLayer || (layer == bodyLayer && occluderRenderer.sortingOrder <= _mainRenderer.sortingOrder)) continue;

            // 지정된 레이어(기본: 벽/장애물)에 속한 오브젝트만 가림막으로 인정. 몬스터 등은 제외됨.
            if (((1 << occluderRenderer.gameObject.layer) & occluderLayers.value) == 0) continue;


            if (!occluderRenderer.bounds.Intersects(myBounds)) continue;
            SpriteMask mask = GetMask(used++);
            mask.sprite = occluderRenderer.sprite;
            mask.alphaCutoff = maskAlphaCutoff;
            mask.frontSortingLayerID = mask.backSortingLayerID = silhouetteRenderer.sortingLayerID;
            mask.frontSortingOrder = silhouetteRenderer.sortingOrder + 1;
            mask.backSortingOrder = silhouetteRenderer.sortingOrder - 1;
            mask.transform.SetPositionAndRotation(occluderRenderer.transform.position, occluderRenderer.transform.rotation);
            Vector3 parentScale = mask.transform.parent.lossyScale;
            Vector3 scale = occluderRenderer.transform.lossyScale;
            mask.transform.localScale = new Vector3(
                scale.x / parentScale.x * (occluderRenderer.flipX ? -1f : 1f),
                scale.y / parentScale.y * (occluderRenderer.flipY ? -1f : 1f), 1f);
            mask.enabled = true;
        }
        HideMasks(used);
        return used > 0;
    }

    private SpriteMask GetMask(int index)
    {
        if (index == _masks.Count)
        {
            var go = new GameObject("Occluder Pixel Mask");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            var mask = go.AddComponent<SpriteMask>();
            mask.isCustomRangeActive = true;
            _masks.Add(mask);
        }
        return _masks[index];
    }

    private void HideMasks(int start)
    {
        for (int i = start; i < _masks.Count; i++)
            if (_masks[i] != null) _masks[i].enabled = false;
    }

    private void OnDisable()
    {
        _visibility = 0f;
        HideMasks(0);
        if (silhouetteRenderer != null) silhouetteRenderer.enabled = false;
    }
}
