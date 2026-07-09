using UnityEngine;

public class AutoDestroyEffect : MonoBehaviour
{
    void Start()
    {
        var animator = GetComponent<Animator>();
        float length = animator.runtimeAnimatorController.animationClips[0].length;
        Destroy(gameObject, length);
    }
}
