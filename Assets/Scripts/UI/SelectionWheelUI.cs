using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class SelectionWheelUI : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float radius = 200f;
    [SerializeField] private Color normalColor = new Color(0, 0, 0, 0.4f);
    [SerializeField] private Color highlightColor = new Color(0.2f, 0.6f, 1f, 0.8f);
    [SerializeField] private float dragThreshold = 40f;

    [Header("References")]
    [SerializeField] private RectTransform container;
    [SerializeField] private Sprite circleSprite; // 여기에 위에서 만든 Circle 스프라이트를 넣으세요!
    
    private List<Image> _segments = new List<Image>();
    private List<TextMeshProUGUI> _labelTexts = new List<TextMeshProUGUI>();
    private Vector2 _centerPos;
    private int _currentIndex = -1;
    private List<CommandData> _currentTypes;

    public void Show(Vector2 screenPos, List<CommandData> types)
    {
        gameObject.SetActive(true);
        container.position = screenPos;
        _centerPos = screenPos;
        _currentTypes = types;

        SetupSegments(types);
        UpdateHighlight(screenPos);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        _currentIndex = -1;
    }

    private void SetupSegments(List<CommandData> types)
    {
        int count = types.Count;
        float fillAmount = 1f / count;
        float anglePerSegment = 360f / count;

        for (int i = 0; i < Mathf.Max(count, _segments.Count); i++)
        {
            if (i >= _segments.Count)
            {
                // 조각 생성
                GameObject obj = new GameObject($"Segment_{i}", typeof(RectTransform), typeof(Image));
                obj.transform.SetParent(container, false);
                Image img = obj.GetComponent<Image>();
                img.sprite = circleSprite; // 원형 스프라이트 할당
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Radial360;
                img.fillOrigin = (int)Image.Origin360.Top;
                img.fillAmount = fillAmount;
                img.fillClockwise = true;
                
                // 텍스트 생성
                GameObject textObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObj.transform.SetParent(container, false); // 텍스트는 회전 안되게 container의 직접 자식으로
                TextMeshProUGUI txt = textObj.GetComponent<TextMeshProUGUI>();
                txt.fontSize = 18;
                txt.fontStyle = FontStyles.Bold;
                txt.alignment = TextAlignmentOptions.Center;
                txt.enableWordWrapping = false;
                
                _segments.Add(img);
                _labelTexts.Add(txt);
            }

            if (i < count)
            {
                _segments[i].gameObject.SetActive(true);
                _labelTexts[i].gameObject.SetActive(true);
                
                _segments[i].sprite = circleSprite;
                _segments[i].fillAmount = fillAmount;
                
                // 조각 회전: 12시 방향이 조각의 정중앙이 되도록 보정
                // fillOrigin이 Top(12시)이므로, 첫 조각을 시계 반대방향으로 1/2 조각만큼 미리 회전시켜둠
                float offsetRotation = anglePerSegment / 2f;
                _segments[i].rectTransform.localRotation = Quaternion.Euler(0, 0, offsetRotation - (i * anglePerSegment));
                _segments[i].rectTransform.sizeDelta = new Vector2(radius * 2, radius * 2);
                
                _segments[i].color = normalColor;
                _labelTexts[i].text = GetShortName(types[i]);
                
                // 텍스트 위치: 각 조각의 중앙 각도 계산
                float labelAngle = (i * anglePerSegment) * Mathf.Deg2Rad;
                _labelTexts[i].rectTransform.localPosition = new Vector3(Mathf.Sin(labelAngle), Mathf.Cos(labelAngle), 0) * (radius * 0.65f);
            }
            else
            {
                _segments[i].gameObject.SetActive(false);
                _labelTexts[i].gameObject.SetActive(false);
            }
        }
    }

    public void UpdateHighlight(Vector2 currentMousePos)
    {
        float dist = Vector2.Distance(_centerPos, currentMousePos);
        
        if (dist < dragThreshold)
        {
            ResetHighlights();
            _currentIndex = -1;
            return;
        }

        Vector2 dir = currentMousePos - _centerPos;
        float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg; // 12시가 0도
        if (angle < 0) angle += 360f;

        int count = _currentTypes.Count;
        float anglePerSegment = 360f / count;
        
        // 인덱스 계산 (반올림을 통해 각도 범위 매칭)
        int index = Mathf.RoundToInt(angle / anglePerSegment) % count;

        if (_currentIndex != index)
        {
            _currentIndex = index;
            for (int i = 0; i < _segments.Count; i++)
            {
                if (i < count)
                {
                    _segments[i].color = (i == _currentIndex) ? highlightColor : normalColor;
                    _labelTexts[i].color = (i == _currentIndex) ? Color.white : new Color(1,1,1,0.5f);
                    _labelTexts[i].rectTransform.localScale = (i == _currentIndex) ? Vector3.one * 1.2f : Vector3.one;
                }
            }
        }
    }

    private void ResetHighlights()
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            _segments[i].color = normalColor;
            if (i < _labelTexts.Count)
            {
                _labelTexts[i].color = new Color(1,1,1,0.5f);
                _labelTexts[i].rectTransform.localScale = Vector3.one;
            }
        }
    }

    public int GetSelectedIndex() => _currentIndex;

    private string GetShortName(CommandData type)
    {
        return type.ToString().Replace("Skeleton", "");
    }
}
