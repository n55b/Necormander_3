using UnityEngine;

/// <summary>
/// Animator가 재생 중인 현재 스테이트(클립) 길이만큼 대기한 뒤 자동으로 오브젝트를 파괴합니다.
/// 걷기 먼지 이펙트처럼 한 번 재생하고 사라져야 하는 프리팹에 붙여서 사용합니다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AutoDestroyOnAnimatorEnd : MonoBehaviour
{
    private Animator _animator;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void Start()
    {
        float length = _animator.GetCurrentAnimatorStateInfo(0).length;
        Destroy(gameObject, length);
    }
}
