using System;
using UnityEngine;

/// <summary>월드 그림의 투명도만 보간한다. 정지한 그림에는 프레임 갱신 비용이 없다.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
public sealed class SpriteVisibility : MonoBehaviour
{
    private SpriteRenderer picture;
    private float from, to, duration, elapsed, fullAlpha;
    private Action finished;

    public static SpriteVisibility For(SpriteRenderer picture) =>
        picture.TryGetComponent<SpriteVisibility>(out var fade) ? fade : picture.gameObject.AddComponent<SpriteVisibility>();

    public static void Reveal(GameObject root, float seconds)
    {
        if (root.TryGetComponent<SpriteRenderer>(out var sprite)) For(sprite).Reveal(seconds);
    }

    public static void RetireInteractable(GameObject root, float seconds)
    {
        foreach (var col in root.GetComponents<Collider2D>()) col.enabled = false;
        if (root.TryGetComponent<SpriteRenderer>(out var sprite))
            For(sprite).FadeOut(seconds, () => Destroy(root));
        else Destroy(root);
    }

    private void Awake()
    {
        picture = GetComponent<SpriteRenderer>();
        fullAlpha = picture.color.a;
        enabled = false;
    }

    public void Reveal(float seconds)
    {
        SetAlpha(0f);
        Begin(fullAlpha, seconds, null);
    }

    public void FadeOut(float seconds, Action onHidden = null) => Begin(0f, seconds, onHidden);

    private void Begin(float target, float seconds, Action onComplete)
    {
        from = picture.color.a;
        to = target;
        duration = Mathf.Max(.01f, seconds);
        elapsed = 0f;
        finished = onComplete;
        enabled = true;
    }

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        SetAlpha(Mathf.SmoothStep(from, to, elapsed / duration));
        if (elapsed < duration) return;
        var callback = finished;
        finished = null;
        enabled = false;
        callback?.Invoke();
    }

    private void SetAlpha(float alpha)
    {
        var color = picture.color;
        color.a = alpha;
        picture.color = color;
    }
}
