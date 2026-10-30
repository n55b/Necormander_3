using UnityEngine;

/// <summary>자식 목록이 바뀌면 알려준다. UIKeepOnTop 이 실행 중에 부모에 자동으로 붙인다.</summary>
[AddComponentMenu("")]
public class UIChildrenWatcher : MonoBehaviour
{
    public event System.Action Changed;
    private void OnTransformChildrenChanged() => Changed?.Invoke();
}
