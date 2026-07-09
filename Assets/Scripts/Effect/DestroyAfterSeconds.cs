using UnityEngine;

/// <summary>
/// 지정된 시간이 지나면 자동으로 오브젝트를 파괴합니다.
/// Animator 없이 순수 파티클/이펙트만 있는 프리팹에 붙여서 사용합니다.
/// (Animator 기반 이펙트는 AutoDestroyEffect를 사용하세요.)
/// </summary>
public class DestroyAfterSeconds : MonoBehaviour
{
    [SerializeField] private float lifetime = 1f;

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }
}
