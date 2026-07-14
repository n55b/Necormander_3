using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// TextEffectParser가 뽑아낸 TextEffectRange들을 받아서, TMP 텍스트의 글자(vertex) 단위로
/// 실제 애니메이션을 재생한다.
///
/// - Punch, Shake  : 1회성. 텍스트가 세팅되는 순간부터 짧게 재생되고 끝나면 자동으로 꺼진다.
/// - Wave, Rainbow, Jitter : 반복성. ApplyEffects가 다시 호출되거나 ClearEffects가 호출될 때까지 계속 재생된다.
///
/// 매 프레임 ForceMeshUpdate()로 "이펙트 적용 전" 원본 정점 위치를 복원한 뒤 그 위에 오프셋을 더하는 방식이라
/// 누적 오차 없이 안전하게 반복 재생할 수 있다. (TMP 공식 예제인 WarpTextExample과 동일한 접근)
/// </summary>
[DisallowMultipleComponent]
public class TMPTextEffectPlayer : MonoBehaviour
{
    [Header("Wave")]
    [SerializeField] private float waveAmplitude = 6f;
    [SerializeField] private float waveFrequency = 2f;

    [Header("Jitter")]
    [SerializeField] private float jitterAmplitude = 3f;
    [SerializeField] private float jitterSpeed = 20f;

    [Header("Shake (per-char, one-shot)")]
    [SerializeField] private float shakeAmplitude = 4f;
    [SerializeField] private float shakeDuration = 0.4f;

    [Header("Punch (per-char, one-shot)")]
    [SerializeField] private float punchScale = 0.5f;
    [SerializeField] private float punchDuration = 0.35f;

    [Header("Rainbow")]
    [SerializeField] private float rainbowSaturation = 0.85f;
    [SerializeField] private float rainbowSpeed = 1f;

    private TMP_Text tmp;
    private readonly List<TextEffectRange> activeRanges = new List<TextEffectRange>();
    private float startTime;
    private bool hasContinuousEffect;
    private float oneShotEndTime;

    private void Awake()
    {
        tmp = GetComponent<TMP_Text>();
    }

    /// <summary>
    /// 새 텍스트가 세팅될 때 호출. 이전 이펙트는 모두 대체된다.
    /// ranges가 비어있으면 이펙트 재생을 끄고 원본 메시로 되돌린다.
    /// </summary>
    public void ApplyEffects(List<TextEffectRange> ranges)
    {
        activeRanges.Clear();
        activeRanges.AddRange(ranges);
        startTime = Time.time;

        hasContinuousEffect = false;
        float maxOneShot = 0f;
        foreach (var r in activeRanges)
        {
            if (r.type == TextEffectType.Wave || r.type == TextEffectType.Rainbow || r.type == TextEffectType.Jitter)
            {
                hasContinuousEffect = true;
            }
            else
            {
                float dur = r.type == TextEffectType.Punch
                    ? punchDuration / Mathf.Max(r.param, 0.01f)
                    : shakeDuration / Mathf.Max(r.param, 0.01f);
                if (dur > maxOneShot) maxOneShot = dur;
            }
        }
        oneShotEndTime = startTime + maxOneShot;

        bool shouldRun = activeRanges.Count > 0;
        enabled = shouldRun;

        if (!shouldRun)
        {
            RestoreOriginalMesh();
        }
    }

    /// <summary>모든 이펙트를 즉시 끄고 원본 모양으로 되돌린다.</summary>
    public void ClearEffects()
    {
        activeRanges.Clear();
        enabled = false;
        RestoreOriginalMesh();
    }

    private void RestoreOriginalMesh()
    {
        if (tmp == null) return;
        tmp.ForceMeshUpdate();
    }

    private void LateUpdate()
    {
        if (tmp == null || activeRanges.Count == 0)
        {
            enabled = false;
            return;
        }

        tmp.ForceMeshUpdate();
        TMP_TextInfo textInfo = tmp.textInfo;
        if (textInfo.characterCount == 0)
        {
            enabled = false;
            return;
        }

        float t = Time.time - startTime;

        foreach (var range in activeRanges)
        {
            int endIndex = Mathf.Min(range.startIndex + range.length, textInfo.characterCount);
            for (int i = range.startIndex; i < endIndex; i++)
            {
                if (i < 0 || i >= textInfo.characterCount) continue;

                TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
                if (!charInfo.isVisible) continue;

                int matIndex = charInfo.materialReferenceIndex;
                int vIndex = charInfo.vertexIndex;
                Vector3[] verts = textInfo.meshInfo[matIndex].vertices;
                Vector3 charMid = (verts[vIndex] + verts[vIndex + 2]) * 0.5f;

                Vector3 offset = Vector3.zero;
                float scaleMul = 1f;
                Color32? colorOverride = null;

                switch (range.type)
                {
                    case TextEffectType.Wave:
                        offset = new Vector3(
                            0f,
                            Mathf.Sin(t * waveFrequency * range.param + i * 0.5f) * waveAmplitude,
                            0f);
                        break;

                    case TextEffectType.Jitter:
                        offset = new Vector3(
                            (Mathf.PerlinNoise(i * 10.7f, t * jitterSpeed * range.param) - 0.5f) * 2f * jitterAmplitude,
                            (Mathf.PerlinNoise(i * 3.3f + 50f, t * jitterSpeed * range.param) - 0.5f) * 2f * jitterAmplitude,
                            0f);
                        break;

                    case TextEffectType.Rainbow:
                        float hue = Mathf.Repeat(t * rainbowSpeed * range.param + i * 0.08f, 1f);
                        colorOverride = Color.HSVToRGB(hue, rainbowSaturation, 1f);
                        break;

                    case TextEffectType.Shake:
                    {
                        float dur = shakeDuration / Mathf.Max(range.param, 0.01f);
                        float k = Mathf.Clamp01(1f - t / dur);
                        if (k > 0f)
                        {
                            offset = new Vector3(
                                (Mathf.PerlinNoise(i * 12.1f, t * 40f) - 0.5f) * 2f * shakeAmplitude * k,
                                (Mathf.PerlinNoise(i * 5.5f + 90f, t * 40f) - 0.5f) * 2f * shakeAmplitude * k,
                                0f);
                        }
                        break;
                    }

                    case TextEffectType.Punch:
                    {
                        float dur = punchDuration / Mathf.Max(range.param, 0.01f);
                        float k = Mathf.Clamp01(t / dur);
                        // 0 -> 1 -> 0 으로 튀었다가 원래 크기로 돌아오는 커브
                        float curve = Mathf.Sin(k * Mathf.PI) * (1f - k);
                        scaleMul = 1f + curve * punchScale * range.param;
                        break;
                    }
                }

                for (int j = 0; j < 4; j++)
                {
                    Vector3 v = verts[vIndex + j];
                    if (!Mathf.Approximately(scaleMul, 1f))
                        v = charMid + (v - charMid) * scaleMul;
                    v += offset;
                    verts[vIndex + j] = v;
                }

                if (colorOverride.HasValue)
                {
                    Color32[] colors = textInfo.meshInfo[matIndex].colors32;
                    Color32 c = colorOverride.Value;
                    for (int j = 0; j < 4; j++)
                        colors[vIndex + j] = c;
                }
            }
        }

        for (int m = 0; m < textInfo.meshInfo.Length; m++)
        {
            textInfo.meshInfo[m].mesh.vertices = textInfo.meshInfo[m].vertices;
            textInfo.meshInfo[m].mesh.colors32 = textInfo.meshInfo[m].colors32;
            tmp.UpdateGeometry(textInfo.meshInfo[m].mesh, m);
        }

        // 반복 이펙트가 하나도 없고, 1회성 이펙트가 다 끝났으면 재생을 멈춘다.
        if (!hasContinuousEffect && Time.time > oneShotEndTime)
        {
            enabled = false;
        }
    }
}
