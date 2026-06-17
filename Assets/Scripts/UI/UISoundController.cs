using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UIEventBus를 구독해서 UI별 사운드를 재생하는 컨트롤러.
///
/// 핵심 규칙:
///   SetReady()가 호출되기 전(게임 로딩 중)에는 모든 사운드를 차단합니다.
///   GameManager에서 플레이어 스폰 완료 후 UISoundController.SetReady() 호출 필요.
/// </summary>
public class UISoundController : MonoBehaviour
{
    public static UISoundController Instance;

    [Serializable]
    public class UISoundEntry
    {
        [Tooltip("UIEventBus.NotifyOpen/Close에서 넘기는 이름과 일치해야 합니다")]
        public string uiName;
        public AudioClip openClip;
        public AudioClip closeClip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    [Header("기본 클립 (uiName 미등록 시 폴백)")]
    [SerializeField] private AudioClip defaultOpenClip;
    [SerializeField] private AudioClip defaultCloseClip;
    [Range(0f, 1f)] [SerializeField] private float defaultVolume = 0.5f;

    [Header("UI별 클립 등록")]
    [SerializeField] private List<UISoundEntry> entries = new List<UISoundEntry>();

    private Dictionary<string, UISoundEntry> _map;

    // 로딩 중 차단 플래그 — GameManager.SetReady() 전까지 false
    private bool _ready;

    // ── 외부 접근 ────────────────────────────────────────────────────
    /// <summary>GameManager에서 플레이어 스폰 완료 후 호출합니다.</summary>
    public static void SetReady()
    {
        if (Instance != null) Instance._ready = true;
    }

    /// <summary>UISound 컴포넌트에서 같은 게이트를 공유합니다.</summary>
    public static bool GetIsReady()
    {
        return Instance != null && Instance._ready;
    }

    // ─────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        _map = new Dictionary<string, UISoundEntry>(entries.Count);
        foreach (var e in entries)
            if (!string.IsNullOrEmpty(e.uiName))
                _map[e.uiName] = e;

        _ready = false; // 로딩 완료 전까지 차단
    }

    private void OnEnable()
    {
        UIEventBus.OnUIOpen  += OnUIOpen;
        UIEventBus.OnUIClose += OnUIClose;
    }

    private void OnDisable()
    {
        UIEventBus.OnUIOpen  -= OnUIOpen;
        UIEventBus.OnUIClose -= OnUIClose;
    }

    // ─────────────────────────────────────────────────────────────────
    private void OnUIOpen(string uiName)
    {
        if (!_ready || SoundManager.Instance == null) return;

        if (_map.TryGetValue(uiName, out var entry) && entry.openClip != null)
            SoundManager.Instance.PlaySFX(entry.openClip, entry.volume);
        else if (defaultOpenClip != null)
            SoundManager.Instance.PlaySFX(defaultOpenClip, defaultVolume);
    }

    private void OnUIClose(string uiName)
    {
        if (!_ready || SoundManager.Instance == null) return;

        if (_map.TryGetValue(uiName, out var entry) && entry.closeClip != null)
            SoundManager.Instance.PlaySFX(entry.closeClip, entry.volume);
        else if (defaultCloseClip != null)
            SoundManager.Instance.PlaySFX(defaultCloseClip, defaultVolume);
    }
}
