using System.Collections.Generic;
using UnityEngine;

public class DebuffPool : MonoBehaviour
{
    public static DebuffPool Instance;

    [SerializeField] private GameObject iconPrefab; // DebuffIcon 스크립트가 붙은 프리팹
    private Stack<DebuffIcon> pool = new Stack<DebuffIcon>();

    void Awake() => Instance = this;

    public DebuffIcon Pop()
    {
        if (pool.Count > 0)
        {
            DebuffIcon icon = pool.Pop();
            icon.gameObject.SetActive(true);
            return icon;
        }
        return Instantiate(iconPrefab).GetComponent<DebuffIcon>();
    }

    public void Push(DebuffIcon icon)
    {
        icon.gameObject.SetActive(false);
        icon.transform.SetParent(this.transform, false); // 관리 편의를 위해 풀 밑으로 이동 (스케일 변조 방지)
        icon.transform.localScale = Vector3.one; // 크기 리셋
        pool.Push(icon);
    }
}