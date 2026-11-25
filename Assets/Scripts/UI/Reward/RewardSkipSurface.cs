using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed class RewardSkipSurface : RewardPixelSurface
{
    [SerializeField] private RectTransform caption;
    private float emphasis;
    private bool pressed;
    private bool muted;
    private Coroutine transition;

    public void SetAppearance(bool focused, bool down, bool disabled, bool instant)
    {
        if (transition != null) StopCoroutine(transition);
        transition = null;
        pressed = down;
        muted = disabled;
        if (caption != null)
        {
            var p = caption.anchoredPosition;
            caption.anchoredPosition = new Vector2(p.x, down ? -1 : 0);
        }
        float target = focused ? 1 : 0;
        if (instant || !Application.isPlaying || !isActiveAndEnabled || !UISkinSettings.MotionOn || Mathf.Approximately(emphasis, target))
            emphasis = target;
        else
            transition = StartCoroutine(Fade(target));
        SetVerticesDirty();
    }

    private IEnumerator Fade(float target)
    {
        while (emphasis != target)
        {
            yield return null;
            emphasis = Mathf.MoveTowards(emphasis, target, Time.unscaledDeltaTime * 8);
            SetVerticesDirty();
        }
        transition = null;
    }

    protected override void OnDisable()
    {
        if (transition != null) StopCoroutine(transition);
        transition = null;
        base.OnDisable();
    }

    protected override void Draw(VertexHelper mesh, float w, float h)
    {
        Color32 face = new(48, 45, 52, 255);
        Color32 rim = Color.Lerp(new Color32(80, 75, 84, 255), new Color32(168, 160, 168, 255), emphasis);
        Color32 metal = new(224, 217, 222, 255);
        float lift = pressed ? 0 : 1;
        Plate(mesh, 0, lift, w, h - 1, 3, muted ? face : rim);
        Plate(mesh, 1, lift + 1, w - 2, h - 3, 2, face);
        float cy = h * .5f;
        float seam = w - 27;
        int travel = Mathf.RoundToInt(emphasis * 2);
        for (int i = 0; i < 4; i++)
        {
            float x = seam + 8 + i + travel;
            Bar(mesh, x, cy + 3 - i, 2, 1, muted ? rim : metal);
            if (i < 3) Bar(mesh, x, cy - 3 + i, 2, 1, muted ? rim : metal);
        }
        Bar(mesh, seam + 15 + travel, cy - 3, 1, 7, muted ? rim : metal);
    }
}
