using UnityEngine;

/// <summary>나무의 프레임 순서는 유지하고 시작 시점과 속도만 개체별로 다르게 한다.</summary>
[RequireComponent(typeof(Animator))]
public sealed class AmbientAnimationPhase : MonoBehaviour
{
    [SerializeField, InspectorName("재생 속도 범위")] private Vector2 speedRange = new Vector2(.85f, 1.15f);
    private Animator animator;
    private float speed, originalSpeed;
    private bool randomized;
    private void Start()
    {
        animator = GetComponent<Animator>(); originalSpeed = animator.speed;
        speed = PolishFeedback.VisualRange(Mathf.Max(.01f, speedRange.x), Mathf.Max(.01f, speedRange.y));
        randomized = PolishFeedback.ExistingEnabled(ExistingFeedbackCue.TreeMotion);
        if (randomized) { animator.Play(0, 0, PolishFeedback.VisualRange(0, 1)); animator.speed = speed; }
    }
    private void Update()
    {
        bool next = PolishFeedback.ExistingEnabled(ExistingFeedbackCue.TreeMotion);
        if (animator == null || randomized == next) return;
        randomized = next; animator.speed = next ? speed : originalSpeed;
    }
}
