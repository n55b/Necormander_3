using UnityEngine;

/// <summary>
/// 같은 부모 안에서 항상 맨 앞(마지막 형제)에 그려지게 한다.
/// 나중에 생기는 자식(미니맵의 방 아이콘 등)이 테두리를 덮지 않게 할 때 쓴다.
/// 부모의 자식 목록이 바뀔 때만 순서를 고치므로 매 프레임 비용이 없다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI 스킨/항상 맨 앞에 그리기")]
public class UIKeepOnTop : MonoBehaviour
{
    private Transform _parent;
    private UIChildrenWatcher _watcher;

    private void OnEnable()
    {
        _parent = transform.parent;
        if (_parent == null) return;
        _watcher = _parent.GetComponent<UIChildrenWatcher>();
        if (_watcher == null) _watcher = _parent.gameObject.AddComponent<UIChildrenWatcher>();
        _watcher.Changed += BringToFront;
        BringToFront();
    }

    private void OnDisable()
    {
        if (_watcher != null) _watcher.Changed -= BringToFront;
    }

    private void BringToFront()
    {
        if (this == null || _parent == null) return;
        if (transform.GetSiblingIndex() != _parent.childCount - 1) transform.SetAsLastSibling();
    }

}
