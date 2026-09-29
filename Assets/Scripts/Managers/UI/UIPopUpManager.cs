using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 화면 위에 뜨는 창(팝업)들의 겹침을 관리한다.
///
/// <b>레이어</b>
///   · Window  : 전체맵(M). Modal/System 위에는 못 뜬다. 열리면 Window/Overlay 를 닫는다. 전투 중 불가.
///   · Overlay : 주머니(V), 장착 정보(Tab). 시간·전투가 계속 도는 정보창. 서로 교체된다.
///               Window/System 위에는 못 뜨고, Modal(보상 등) 위에는 얹힌다. 전투 중 가능.
///               게임플레이 입력은 막지 않는다(평타/패리/상호작용은 각 창이 IsOpen 으로 직접 막는다).
///   · Modal   : 보상, 증강, 미니언 습득/교체. 항상 뜨고 아래 Window/Overlay 를 닫는다. 전투 중 가능.
///   · System  : 옵션(ESC), 대화. 맨 위에 쌓이고 아무것도 닫지 않는다. 전투 중 가능.
///
/// <b>규칙</b>
///   · 닫기는 <see cref="Close"/> 에 '내 창'을 넘겨서 한다. 목록에 없는 창이면 아무 일도 안 한다.
///     (예전 ClosePopUpUI 는 누가 부르든 현재 창을 닫아서 다른 창이 닫히는 일이 있었다.)
///   · 매니저가 대신 닫는 경우(Modal 등장, 전투 진입, ESC)에도 정리가 되도록, 닫힐 때 할 일은
///     Open 의 onClosed 콜백에 넣는다. 창이 자기 IsOpen 플래그를 들고 있다면 거기서 끈다.
///   · 창 오브젝트가 밖에서 꺼지면(OnDisable) <see cref="Forget"/> 으로 목록에서만 빼둔다.
///
/// <b>ESC = 뒤로 가기</b> — <see cref="CloseTopByEscape"/>: 맨 위 창이 ESC 로 닫을 수 있으면 그 창만 닫는다.
///   기본값은 Window/Overlay 만. Modal(반드시 골라야 함)·대화는 안 닫힌다. Open 의 escClosable 로 바꿀 수 있다.
///
/// <b>옵션(ESC)</b> — 열려 있는 동안 게임이 멈추고(timeScale 0), 다른 창은 새로 뜨지 않는다.
///   · 키로 여는 창(Window/Overlay)은 거절한다.
///   · 이벤트로 뜨는 창(Modal/System)은 버리면 보상이 사라지므로 보류했다가 옵션이 닫히면 띄운다.
///
/// <b>그 외 창은 시간 정지 없음</b> — 대신 Overlay 가 아닌 창이 하나라도 떠 있으면
/// <see cref="BlocksGameplayInput"/> 이 true 가 되어 플레이어의 이동/공격/대쉬/스킬/패리/상호작용이 막힌다.
/// (V/Tab/ESC/M 같은 창 여닫기 키는 막지 않는다.)
/// </summary>
public class UIPopUpManager : Singleton<UIPopUpManager>
{
    public enum Layer { Window, Overlay, Modal, System }

    private class Entry
    {
        public GameObject root;
        public Layer layer;
        public Action onClosed;
        public bool escClosable;
    }

    private readonly List<Entry> _stack = new List<Entry>();
    private readonly List<Entry> _pending = new List<Entry>(); // 옵션 중에 뜨려던 Modal/System. 옵션이 닫히면 띄운다

    private bool _isOnBattle = false;
    private bool _isInitialized = false; // 중복 초기화 방지용 변수
    private PlayerStateUI _playerStateUI;
    private MapUIManager _mapUIManager;
    private PlayerController _subscribedPlayer;
    private GameObject _optionToken; // 옵션은 추가 씬이라 등록용 대리 오브젝트를 쓴다

    /// <summary>Overlay(주머니/장착 정보)를 뺀 창이 하나라도 떠 있는가. (예전 의미와 같다)</summary>
    public bool IsPopUpActive => _stack.Exists(e => e.layer != Layer.Overlay);
    public bool IsOnBattle => _isOnBattle;

    /// <summary>플레이어의 게임플레이 입력을 막아야 하는가. Overlay 는 제외(전투 중에도 보는 정보창).</summary>
    public bool BlocksGameplayInput => IsPopUpActive;

    /// <summary>열려 있거나, 옵션이 닫히기를 기다리는(보류) 중이면 true.</summary>
    public bool IsOpen(GameObject root) => IndexOf(root) >= 0 || PendingIndexOf(root) >= 0;

    /// <summary>ESC 옵션이 열려 있는가. 이 동안 게임은 멈춰 있다.</summary>
    public bool IsOptionOpen => IndexOf(_optionToken) >= 0;

    protected override void OnAwake()
    {
        _playerStateUI = GetComponentInChildren<PlayerStateUI>();
        _mapUIManager = GetComponentInChildren<MapUIManager>();

        _optionToken = new GameObject("OptionPopupToken");
        _optionToken.transform.SetParent(transform, false);
        _optionToken.SetActive(false);
    }

    private void Update()
    {
        if (_isInitialized) return;
        if (GameManager.Instance != null && GameManager.Instance.IsPlayerReady)
        {
            Initialize();
        }
    }

    // ─── 열기 / 닫기 ─────────────────────────────────────────────────
    /// <summary>
    /// 창을 연다(SetActive 도 여기서 한다). 규칙상 못 여는 상황이면 false 를 돌려주고 아무것도 켜지 않는다.
    /// 이미 열려 있으면 true. 옵션 중에 Modal/System 을 열면 보류하고 true(옵션이 닫히면 뜬다).
    /// </summary>
    /// <param name="onClosed">어떤 경로로든(직접 Close, 매니저가 대신 닫음, ESC) 닫힌 직후 한 번 호출.</param>
    /// <param name="escClosable">ESC 로 닫히는가. 생략하면 Window/Overlay 만 true.</param>
    public bool Open(GameObject root, Layer layer, Action onClosed = null, bool? escClosable = null)
    {
        if (root == null) return false;
        if (IsOpen(root)) return true;

        var entry = new Entry
        {
            root = root, layer = layer, onClosed = onClosed,
            escClosable = escClosable ?? (layer == Layer.Window || layer == Layer.Overlay),
        };

        // ESC 옵션 중에는 다른 창을 띄우지 않는다.
        if (IsOptionOpen && root != _optionToken)
        {
            if (layer == Layer.Window || layer == Layer.Overlay) return false; // 키로 여는 창: 거절
            root.SetActive(false);                                           // 이벤트성 창: 보류
            _pending.Add(entry);
            return true;
        }

        switch (layer)
        {
            case Layer.Window:
                if (_isOnBattle) return false;
                if (HasLayer(Layer.Modal) || HasLayer(Layer.System)) return false;
                CloseWhere(e => e.layer == Layer.Window || e.layer == Layer.Overlay);
                break;
            case Layer.Overlay:
                if (HasLayer(Layer.Window) || HasLayer(Layer.System)) return false;
                CloseWhere(e => e.layer == Layer.Overlay); // 주머니 ↔ 장착 정보는 교체
                break;
            case Layer.Modal:
                CloseWhere(e => e.layer == Layer.Window || e.layer == Layer.Overlay);
                break;
        }

        root.SetActive(true);
        _stack.Add(entry);
        return true;
    }

    /// <summary>내 창을 닫는다(보류 중이면 보류 취소). 목록에 없는 창이면 아무 일도 안 한다.</summary>
    public void Close(GameObject root)
    {
        int p = PendingIndexOf(root);
        if (p >= 0)
        {
            var pe = _pending[p];
            _pending.RemoveAt(p);
            pe.onClosed?.Invoke();
            return;
        }

        int i = IndexOf(root);
        if (i < 0) return;

        var e = _stack[i];
        _stack.RemoveAt(i);
        if (e.root != null) e.root.SetActive(false);
        e.onClosed?.Invoke();
    }

    /// <summary>
    /// 창이 밖에서 이미 꺼졌을 때(OnDisable, 씬 종료) 목록에서만 뺀다.
    /// SetActive 도 onClosed 도 부르지 않는다 — 비활성화 도중에 계층을 건드리면 에러가 난다.
    /// </summary>
    public void Forget(GameObject root)
    {
        int p = PendingIndexOf(root);
        if (p >= 0) _pending.RemoveAt(p);
        int i = IndexOf(root);
        if (i >= 0) _stack.RemoveAt(i);
    }

    /// <summary>
    /// ESC = 뒤로 가기. 맨 위 창이 ESC 로 닫을 수 있으면 그 창만 닫고 true.
    /// 창이 없거나 맨 위가 못 닫는 창(보상 등)이면 아무것도 안 하고 false — 이때 호출하는 쪽이 옵션을 연다.
    /// </summary>
    public bool CloseTopByEscape()
    {
        if (_stack.Count == 0) return false;
        var top = _stack[_stack.Count - 1];
        if (!top.escClosable) return false;
        Close(top.root);
        return true;
    }

    /// <summary>모든 창을 닫는다(보류 중인 것 포함). 씬 이탈 등 정말 전부 정리해야 할 때만.</summary>
    public void CloseAll()
    {
        var pending = new List<Entry>(_pending);
        foreach (var p in pending) Close(p.root);
        CloseWhere(_ => true);
    }

    // ─── 옵션 (추가 씬) ─────────────────────────────────────────────
    /// <summary>옵션을 열고 게임을 멈춘다.</summary>
    public bool OpenOption()
    {
        if (IsOptionOpen) return true;
        if (!Open(_optionToken, Layer.System, null, escClosable: false)) return false; // ESC 닫기는 SceneOptionManager 가 처리
        if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(true);
        return true;
    }

    /// <summary>옵션을 닫고 게임을 재개한 뒤, 옵션 중에 보류된 창을 띄운다.</summary>
    public void CloseOption()
    {
        if (!IsOptionOpen) return;
        Close(_optionToken);
        if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(false);

        var pending = new List<Entry>(_pending);
        _pending.Clear();
        foreach (var e in pending) Open(e.root, e.layer, e.onClosed, e.escClosable);
    }

    // ─── 예전 API (호환용) ──────────────────────────────────────────
    [Obsolete("Open(root, UIPopUpManager.Layer.Window) 를 쓰세요.")]
    public void PopUpUI(GameObject popUpObj) => Open(popUpObj, Layer.Window);

    [Obsolete("Open(root, UIPopUpManager.Layer.Modal) 를 쓰세요.")]
    public void ForcePopUpUI(GameObject popUpObj) => Open(popUpObj, Layer.Modal);

    [Obsolete("Close(root) 를 쓰세요. 이건 맨 위의 System 이 아닌 창을 닫습니다.")]
    public void ClosePopUpUI()
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (_stack[i].layer == Layer.System) continue;
            Close(_stack[i].root);
            return;
        }
    }

    [Obsolete("OpenOption() 을 쓰세요.")]
    public bool PushOptionKey() => OpenOption();

    // ─── 내부 ────────────────────────────────────────────────────────
    private int IndexOf(GameObject root)
    {
        if (root == null) return -1;
        for (int i = _stack.Count - 1; i >= 0; i--)
            if (_stack[i].root == root) return i;
        return -1;
    }

    private int PendingIndexOf(GameObject root)
    {
        if (root == null) return -1;
        for (int i = _pending.Count - 1; i >= 0; i--)
            if (_pending[i].root == root) return i;
        return -1;
    }

    private bool HasLayer(Layer layer) => _stack.Exists(e => e.layer == layer);

    private void CloseWhere(Predicate<Entry> match)
    {
        // onClosed 콜백이 목록을 건드릴 수 있으니 대상만 먼저 뽑아 두고 닫는다.
        var targets = _stack.FindAll(match);
        for (int i = targets.Count - 1; i >= 0; i--) Close(targets[i].root);
    }

    // ─── 전투 / 비전투 ───────────────────────────────────────────────
    private void HandleEnterBattle()
    {
        _isOnBattle = true;
        CloseWhere(e => e.layer == Layer.Window); // 정보창(Overlay)은 전투 중에도 유지
        if (_playerStateUI != null) _playerStateUI.CloseStateUI();
        if (_mapUIManager != null) _mapUIManager.CloseMapUI();
    }

    private void HandleEnterIdle()
    {
        _isOnBattle = false;
        if (_playerStateUI != null) _playerStateUI.PopUpStateUI();
    }

    /// <summary>
    /// 초기화 메서드. 이벤트 구독 등
    /// </summary>
    private void Initialize()
    {
        _isInitialized = true;

        _subscribedPlayer = GameManager.Instance.PLAYERCONTROLLER;
        if (_subscribedPlayer == null) return;
        _subscribedPlayer.OnEnterIdle += HandleEnterIdle;
        _subscribedPlayer.OnEnterBattle += HandleEnterBattle;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // 람다로 구독하면 해제가 안 된다(매번 새 대리자). 메서드로 구독/해제한다.
        if (_subscribedPlayer != null)
        {
            _subscribedPlayer.OnEnterIdle -= HandleEnterIdle;
            _subscribedPlayer.OnEnterBattle -= HandleEnterBattle;
            _subscribedPlayer = null;
        }
    }
}