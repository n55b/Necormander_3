using System;
using UnityEngine;

/// <summary>
/// 소리 묶음: 여러 클립 중 무작위 하나 + 볼륨 + 음높이 범위.
/// 오브젝트 소리(사운드 세트)와 UI 소리(UI 소리 설정)가 같이 쓴다.
/// </summary>
[Serializable]
public class SoundClipGroup
{
    [KoLabel("소리 파일들"), Tooltip("여러 개 넣으면 매번 무작위로 하나를 고릅니다. 같은 파일이 연달아 나오지 않게 합니다. 비워 두면 소리가 나지 않습니다.")]
    public AudioClip[] clips = new AudioClip[0];
    [KoLabel("볼륨", 0f, 1f)]
    public float volume = 0.8f;
    [KoLabel("음높이 최소", 0.5f, 2f), Tooltip("1 = 원래 높이. 최소~최대 사이에서 매번 무작위로 정해져 같은 소리도 덜 반복적으로 들립니다.")]
    public float pitchMin = 1f;
    [KoLabel("음높이 최대", 0.5f, 2f)]
    public float pitchMax = 1f;

    [KoLabel("소리 켜기")] public bool enabled = true;
    [KoLabel("음량 변화 폭", 0f, 0.3f), Tooltip("0.1이면 매번 음량이 ±10% 달라집니다.")]
    public float volumeVariation = 0f;
    [KoLabel("묶음별 최소 재생 간격 (초)", 0f, 3f)] public float minPlaybackInterval = 0f;
    [KoLabel("재생 우선순위", 0, 256), Tooltip("작을수록 중요합니다. 풀이 가득 차면 낮은 우선순위의 소리부터 양보합니다. 중요 신호 32, 일반 공격 128, 발소리 200.")]
    public int priority = 128;
    [KoLabel("들리는 거리", 0f, 60f), Tooltip("월드 소리만 적용. 0이면 거리 제한 없음. 화면 밖 먼 적의 소리가 한꺼번에 겹치는 것을 줄입니다.")]
    public float audibleRange = 0f;

    [NonSerialized] private int _lastIndex = -1;
    [NonSerialized] private float _nextPlay;
    [NonSerialized] private int _session = -1;
    private static int session;
    public AudioClip LastPlayedClip { get; private set; }
    private static readonly System.Random variation = new System.Random();
    private static float Range(float a, float b) => a + (b - a) * (float)variation.NextDouble();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => session++;

    public bool HasClip
    {
        get
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return true;
            return false;
        }
    }

    /// <summary>무작위 클립 (직전과 같은 것 피함). 없으면 null.</summary>
    public AudioClip PickClip()
    {
        if (clips == null || clips.Length == 0) return null;
        if (clips.Length == 1) return clips[0];
        int i = variation.Next(_lastIndex >= 0 && _lastIndex < clips.Length ? clips.Length - 1 : clips.Length);
        if (_lastIndex >= 0 && _lastIndex < clips.Length && i >= _lastIndex) i++;
        _lastIndex = i;
        if (clips[i] != null) return clips[i];
        for (int k = 0; k < clips.Length; k++) if (clips[k] != null) return clips[k];
        return null;
    }

    public float PickPitch()
    {
        float a = Mathf.Min(pitchMin, pitchMax), b = Mathf.Max(pitchMin, pitchMax);
        return Mathf.Approximately(a, b) ? a : Range(a, b);
    }

    /// <summary>지금 바로 재생. 클립이 없으면 false.</summary>
    public bool Play(Vector3? position = null)
        => PlayChosenClip(PickClip(), position);

    public bool PlayChosenClip(AudioClip clip, Vector3? position = null)
    {
        if (_session != session) { _session = session; _nextPlay = 0f; }
        if (!enabled || volume <= 0f || Time.unscaledTime < _nextPlay) return false;
        float gain = volume;
        if (position.HasValue && audibleRange > 0f && PolishFeedback.Player != null)
        {
            float distance = Vector2.Distance(position.Value, PolishFeedback.Player.transform.position);
            gain *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / audibleRange));
            if (gain < .002f) return false;
        }
        if (clip == null) return false;
        if (!PlayClip(clip, gain * (1f + Range(-volumeVariation, volumeVariation)), PickPitch(), priority)) return false;
        _nextPlay = Time.unscaledTime + Mathf.Max(0f, minPlaybackInterval);
        LastPlayedClip = clip;
        return true;
    }

    /// <summary>SoundManager 효과음 풀로 재생(옵션 효과음 볼륨 반영). 없으면 카메라 위치에서 임시 재생.</summary>
    public static bool PlayClip(AudioClip clip, float volume, float pitch, int priority = 128)
    {
        if (clip == null) return false;
        if (SoundManager.Instance != null) return SoundManager.Instance.PlaySFXWithPitch(clip, volume, pitch, priority);
        else
        {
            var cam = Camera.main;
            AudioSource.PlayClipAtPoint(clip, cam != null ? cam.transform.position : Vector3.zero, volume);
            return true;
        }
    }
}
