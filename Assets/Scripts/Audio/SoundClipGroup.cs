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

    [NonSerialized] private int _lastIndex = -1;

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
        int i = UnityEngine.Random.Range(0, clips.Length - 1);
        if (_lastIndex >= 0 && i >= _lastIndex) i++;
        _lastIndex = i;
        if (clips[i] != null) return clips[i];
        for (int k = 0; k < clips.Length; k++) if (clips[k] != null) return clips[k];
        return null;
    }

    public float PickPitch()
    {
        float a = Mathf.Min(pitchMin, pitchMax), b = Mathf.Max(pitchMin, pitchMax);
        return Mathf.Approximately(a, b) ? a : UnityEngine.Random.Range(a, b);
    }

    /// <summary>지금 바로 재생. 클립이 없으면 false.</summary>
    public bool Play()
    {
        var clip = PickClip();
        if (clip == null) return false;
        PlayClip(clip, volume, PickPitch());
        return true;
    }

    /// <summary>SoundManager 효과음 풀로 재생(옵션 효과음 볼륨 반영). 없으면 카메라 위치에서 임시 재생.</summary>
    public static void PlayClip(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;
        if (SoundManager.Instance != null) SoundManager.Instance.PlaySFXWithPitch(clip, volume, pitch);
        else
        {
            var cam = Camera.main;
            AudioSource.PlayClipAtPoint(clip, cam != null ? cam.transform.position : Vector3.zero, volume);
        }
    }
}
