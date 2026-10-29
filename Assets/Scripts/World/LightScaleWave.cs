using UnityEngine;

/// <summary>횃불과 촛불의 빛 크기를 기존 그래프와 같은 파형으로 흔든다.</summary>
public sealed class LightScaleWave : MonoBehaviour
{
    [SerializeField, InspectorName("기준 크기")]
    [Tooltip("빛의 기본 크기입니다. 기존 횃불과 촛불은 1.2입니다.")]
    private Vector3 baseScale = new Vector3(1.2f, 1.2f, 1.2f);

    [SerializeField, InspectorName("흔들림 속도")]
    [Tooltip("빛 크기가 변하는 속도입니다. 기존 그래프의 Speed와 같습니다.")]
    private float speed = 5f;

    [SerializeField, InspectorName("흔들림 크기")]
    [Tooltip("기준 크기 대비 변화 비율입니다. 0.02는 크기가 양쪽으로 2%씩 변합니다.")]
    private float relativeAmplitude = .02f;

    private void Update()
    {
        transform.localScale = baseScale + baseScale * (Mathf.Sin(Time.time * speed) * relativeAmplitude);
    }
}
