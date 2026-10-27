using UnityEngine;

public class AutoDestroyEffect : MonoBehaviour
{
    [SerializeField, Tooltip("Catalog에서 켜고 끌 연출 종류. 연결 안 함은 기존 동작을 유지합니다.")]
    private ExistingFeedbackCue catalogEffect;
    [SerializeField, Min(.01f), Tooltip("그림 애니메이션 재생 배속. 1은 기존 속도입니다.")]
    private float playbackSpeed = 1f;
    void Start()
    {
        var animator = GetComponent<Animator>();
        float length = animator.runtimeAnimatorController.animationClips[0].length;
        animator.speed *= Mathf.Max(.01f, playbackSpeed);
        Destroy(gameObject, length / Mathf.Max(.01f, playbackSpeed));
    }
    private void Update()
    {
        if (catalogEffect != ExistingFeedbackCue.None && !PolishFeedback.ExistingEnabled(catalogEffect))
            Destroy(gameObject);
    }
}
