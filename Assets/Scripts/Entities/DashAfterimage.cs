using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 돌진(고속 이동) 중 스프라이트 잔상(고스트)을 남기는 컴포넌트입니다.
/// Rigidbody2D 속도 또는 프레임 간 실제 이동 속도가 speedThreshold를 넘으면 자동으로 잔상을 방출하므로,
/// AI 패턴 코드 수정 없이 프리팹에 붙이기만 하면 동작합니다.
/// (TrailRenderer 대신 스프라이트 고스팅을 쓰는 이유: 캐릭터 실루엣이 남아야 "빠름"으로 읽히고,
///  개별 SpriteRenderer라 Y-sort 정렬과도 충돌하지 않습니다.)
/// </summary>
public class DashAfterimage : MonoBehaviour
{
    [Header("발동 조건")]
    [Tooltip("이 속도(유닛/초) 이상으로 움직일 때만 잔상을 남깁니다. 평상시 이동속도보다 높게 설정하세요.")]
    [SerializeField] private float speedThreshold = 4.5f;

    [Header("잔상 설정")]
    [Tooltip("잔상 1장 생성 간격(초)")]
    [SerializeField] private float spawnInterval = 0.045f;
    [Tooltip("잔상 1장이 사라지기까지 걸리는 시간(초)")]
    [SerializeField] private float fadeTime = 0.25f;
    [Tooltip("잔상 색상(알파 포함). 엘리트는 붉은 틴트 권장")]
    [SerializeField] private Color tint = new Color(1f, 1f, 1f, 0.45f);

    /// <summary>true로 두면 속도와 무관하게 강제 방출 (AI 패턴에서 수동 제어가 필요할 때만 사용, 선택)</summary>
    public bool ForceEmit { get; set; }

    private SpriteRenderer _source;
    private Rigidbody2D _rb;
    private Vector3 _lastPos;
    private float _timer;
    private readonly Queue<SpriteRenderer> _pool = new Queue<SpriteRenderer>();
    private readonly List<SpriteRenderer> _spawned = new List<SpriteRenderer>();

    private void Awake()
    {
        _source = GetComponent<SpriteRenderer>();
        if (_source == null) _source = GetComponentInChildren<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
        _lastPos = transform.position;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || _source == null) return;

        // Rigidbody 속도(돌진 코드가 linearVelocity를 직접 설정)와 실제 좌표 이동 속도(transform 직접 이동형 대쉬) 둘 다 감지
        float posSpeed = ((transform.position - _lastPos) / dt).magnitude;
        _lastPos = transform.position;
        float rbSpeed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
        bool emitting = ForceEmit || Mathf.Max(posSpeed, rbSpeed) >= speedThreshold;

        if (!emitting)
        {
            _timer = spawnInterval; // 다음 발동 시 첫 장이 즉시 나오도록
            return;
        }

        _timer += dt;
        if (_timer < spawnInterval) return;
        _timer = 0f;
        SpawnGhost();
    }

    private void SpawnGhost()
    {
        SpriteRenderer ghost = _pool.Count > 0 ? _pool.Dequeue() : CreateGhost();
        ghost.gameObject.SetActive(true);
        ghost.sprite = _source.sprite; // 현재 애니메이션 프레임 그대로 복제
        ghost.flipX = _source.flipX;
        ghost.flipY = _source.flipY;
        ghost.color = tint;

        Transform t = ghost.transform;
        t.SetPositionAndRotation(_source.transform.position, _source.transform.rotation);
        t.localScale = _source.transform.lossyScale;

        ghost.sortingLayerID = _source.sortingLayerID;
        ghost.sortingOrder = _source.sortingOrder - 1; // 본체 바로 뒤

        StartCoroutine(FadeAndRecycle(ghost));
    }

    private SpriteRenderer CreateGhost()
    {
        GameObject go = new GameObject(name + "_Ghost");
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        _spawned.Add(sr);
        return sr;
    }

    private IEnumerator FadeAndRecycle(SpriteRenderer ghost)
    {
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            if (ghost == null) yield break;
            Color c = tint;
            c.a = Mathf.Lerp(tint.a, 0f, t / fadeTime);
            ghost.color = c;
            yield return null;
        }
        if (ghost == null) yield break;
        ghost.gameObject.SetActive(false);
        _pool.Enqueue(ghost);
    }

    private void OnDestroy()
    {
        // 고스트는 월드에 스폰되므로, 본체가 죽어도 유령 오브젝트가 남지 않게 정리
        foreach (SpriteRenderer sr in _spawned)
        {
            if (sr != null) Destroy(sr.gameObject);
        }
        _spawned.Clear();
        _pool.Clear();
    }
}
