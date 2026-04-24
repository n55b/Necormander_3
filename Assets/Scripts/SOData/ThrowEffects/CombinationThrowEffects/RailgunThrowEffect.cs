using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 플레이어와 착지 지점 사이에 Sprite를 늘려 직선형 번개를 발사하는 효과입니다.
/// OverlapBoxAll을 사용하여 판정의 정확도를 높였습니다.
/// </summary>
public class RailgunThrowEffect : MonoBehaviour
{
    private Vector2 _targetPos;
    private float _damage;
    private int _shieldBreakCount;
    private LayerMask _enemyLayer;
    private float _beamWidth;
    private GameObject _player;

    [Header("비주얼 설정 (SpriteRenderer)")]
    [SerializeField] private SpriteRenderer beamRenderer;

    public void Initialize(Vector2 targetPos, float damage, int shieldBreak, LayerMask enemyLayer, float beamWidth, int repeatCount)
    {
        _targetPos = targetPos;
        _damage = damage;
        _shieldBreakCount = shieldBreak;
        _enemyLayer = enemyLayer;
        _beamWidth = beamWidth;
        _player = GameObject.FindGameObjectWithTag("Player");

        if (beamRenderer != null)
        {
            beamRenderer.enabled = false;
            // 빔의 두께(Y축) 설정
            Vector3 s = beamRenderer.transform.localScale;
            s.y = _beamWidth;
            beamRenderer.transform.localScale = s;
        }

        // 반복 발사 코루틴 시작
        StartCoroutine(RepeatFireSequence(repeatCount));
    }

    private IEnumerator RepeatFireSequence(int repeatCount)
    {
        for (int i = 0; i <= repeatCount; i++)
        {
            if (_player == null) break;

            Vector2 startPos = _player.transform.position;
            Vector2 direction = _targetPos - startPos;
            float distance = direction.magnitude;
            
            // 각도 계산 (Atan2 사용)
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            // 1. 비주얼 배치
            if (beamRenderer != null)
            {
                beamRenderer.transform.position = (startPos + _targetPos) * 0.5f;
                beamRenderer.transform.rotation = Quaternion.Euler(0, 0, angle);
                
                Vector3 scale = beamRenderer.transform.localScale;
                scale.x = distance;
                beamRenderer.transform.localScale = scale;
                
                beamRenderer.enabled = true;
            }

            // 2. 충돌 판정 (OverlapBoxAll)
            Collider2D[] hitColliders = Physics2D.OverlapBoxAll(
                (startPos + _targetPos) * 0.5f, 
                new Vector2(distance, _beamWidth), 
                angle, 
                _enemyLayer
            );

            foreach (var col in hitColliders)
            {
                // CharacterStat 컴포넌트 찾기 (자신 또는 부모에서 탐색)
                CharacterStat stat = col.GetComponent<CharacterStat>();
                if (stat == null) stat = col.GetComponentInParent<CharacterStat>();

                if (stat != null)
                {
                    stat.BreakShield(_shieldBreakCount);
                    stat.GetDamage(new DamageInfo(_damage, DamageType.Physical, _player));
                    Debug.Log($"<color=blue>[Railgun]</color> Hit: {col.gameObject.name}");
                }
            }

            // 잠깐 보여준 뒤 비주얼 끄기
            yield return new WaitForSeconds(0.1f);
            if (beamRenderer != null) beamRenderer.enabled = false;

            // 다음 발사까지 대기 (0.5초)
            if (i < repeatCount)
            {
                yield return new WaitForSeconds(0.4f);
            }
        }

        Destroy(gameObject);
    }
}
