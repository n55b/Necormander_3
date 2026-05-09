using System.Collections.Generic;
using UnityEngine;

public class FloatingTextManager : MonoBehaviour
{
    public static FloatingTextManager instance { get; private set; }

    public GameObject textPrefab;
    public int poolSize = 20;

    private Queue<TextFloating> textPool = new Queue<TextFloating>();

    private void Awake()
    {
        instance = this;
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
