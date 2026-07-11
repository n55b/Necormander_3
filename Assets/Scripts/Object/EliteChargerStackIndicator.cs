using UnityEngine;
using TMPro;

/// <summary>
/// v1.4 B2 시각화: 엘리트 차저가 기둥 파편에 맞은 횟수(누적 강화 스택)를 체력바 근처에
/// 작은 점(pip)으로 표시합니다. 채워진 점 개수 = 현재 스택 수. 스택마다 보스가 받는 피해가
/// 영구적으로 5%씩 늘어나며(EliteChargerAIPatternSO.AddPillarDamageStack 참고), 이 인디케이터는
/// 그 수치를 시각적으로 보여주기만 할 뿐 별도의 게임 로직은 갖지 않습니다.
/// </summary>
public class EliteChargerStackIndicator : MonoBehaviour
{
    private SpriteRenderer[] _pips;
    private TextMeshPro _percentText;
    private Color _filledColor;
    private Color _emptyColor;

    public void Build(int maxCount, float pipSize, float pipSpacing, Color filledColor, Color emptyColor, TMP_FontAsset font)
    {
        _filledColor = filledColor;
        _emptyColor = emptyColor;
        _pips = new SpriteRenderer[maxCount];

        float totalWidth = (maxCount - 1) * pipSpacing;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < maxCount; i++)
        {
            GameObject pip = new GameObject("StackPip" + i);
            pip.transform.SetParent(transform, false);
            pip.transform.localPosition = new Vector3(startX + i * pipSpacing, 0f, 0f);
            pip.transform.localScale = Vector3.one * pipSize;

            SpriteRenderer sr = pip.AddComponent<SpriteRenderer>();
            sr.sprite = GetOrCreateSquareSprite();
            sr.sortingOrder = 200;
            sr.color = emptyColor;

            _pips[i] = sr;
        }

        GameObject textObj = new GameObject("StackPercentText");
        textObj.transform.SetParent(transform, false);
        textObj.transform.localPosition = new Vector3(0f, -0.34f, 0f);

        _percentText = textObj.AddComponent<TextMeshPro>();
        _percentText.alignment = TextAlignmentOptions.Center;
        _percentText.fontSize = 2.4f;
        _percentText.color = filledColor;
        _percentText.outlineWidth = 0.2f;
        _percentText.outlineColor = Color.black;
        _percentText.sortingOrder = 200;
        _percentText.text = string.Empty;
        if (font != null) _percentText.font = font;

        UpdateStack(0, 0f);
    }

    /// <summary>
    /// currentStack: 현재 채워진 pip 개수. bonusPerStack: 스택 1개당 증가하는 받는 피해 배율(예: 0.05 = 5%).
    /// </summary>
    public void UpdateStack(int currentStack, float bonusPerStack)
    {
        if (_pips == null) return;

        for (int i = 0; i < _pips.Length; i++)
        {
            if (_pips[i] == null) continue;
            _pips[i].color = i < currentStack ? _filledColor : _emptyColor;
        }

        if (_percentText != null)
        {
            _percentText.text = currentStack > 0
                ? $"받는 피해 +{Mathf.RoundToInt(currentStack * bonusPerStack * 100f)}%"
                : string.Empty;
        }
    }

    private static Sprite _cachedSquareSprite;
    private static Sprite GetOrCreateSquareSprite()
    {
        if (_cachedSquareSprite != null) return _cachedSquareSprite;

        int size = 8;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, Color.white);
            }
        }
        tex.Apply();

        _cachedSquareSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedSquareSprite;
    }
}
