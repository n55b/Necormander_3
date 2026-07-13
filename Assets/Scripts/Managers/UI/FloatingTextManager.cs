using System.Collections.Generic;
using UnityEngine;

public class FloatingTextManager : Singleton<FloatingTextManager>
{

    public GameObject textPrefab;
    public int poolSize = 20;

    [Header("디버그/테스트용")]
    [Tooltip("체크 해제하면 데미지 숫자(MISS, 쉴드, 처형 포함) 팝업이 뜨지 않습니다. 힐/상태 텍스트는 영향 없음.")]
    [SerializeField] private bool showDamageNumbers = true;
    public bool ShowDamageNumbers => showDamageNumbers;
    public void SetShowDamageNumbers(bool value) => showDamageNumbers = value;

    private Queue<TextFloating> textPool = new Queue<TextFloating>();

    protected override void OnAwake()
    {
        InitializePool();
    }

    // Queue setting
    void InitializePool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject obj = Instantiate(textPrefab, transform);
            TextFloating text = obj.GetComponent<TextFloating>();
            obj.SetActive(false);
            textPool.Enqueue(text);
        }
    }

    public TextFloating GetFromPool()
    {
        if (textPool.Count == 0)
        {
            InitializePool();
        }

        TextFloating text = textPool.Dequeue();
        text.gameObject.SetActive(true);
        return text;
    }

    public void ReturnToPool(TextFloating text)
    {
        text.gameObject.SetActive(false);
        textPool.Enqueue(text);
    }
}
