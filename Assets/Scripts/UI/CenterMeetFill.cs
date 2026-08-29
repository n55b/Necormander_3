using System;
using UnityEngine;
using UnityEngine.UI;

public class CenterMeetFill : MonoBehaviour
{
    public Image leftFill;   // Fill Origin = Left
    public Image rightFill;  // Fill Origin = Right
    public float duration = 1f;

    private float t;
    private bool isFilling = false;
    private bool completed = false;

    // 진행도가 다 찼을 때(공격 실행 시점) 호출됩니다.
    public event Action OnFillComplete;

    public bool IsFilling => isFilling;

    void Update()
    {
        if(!isFilling) return;
        t += Time.deltaTime / duration;
        float progress = Mathf.Clamp01(t);

        leftFill.fillAmount = progress * 0.5f;
        rightFill.fillAmount = progress * 0.5f;

        if (progress >= 1f && !completed)
        {
            completed = true;
            isFilling = false;
            OnFillComplete?.Invoke();
        }
    }

    // 예고를 시작합니다. 패턴마다 다른 시간을 그대로 넘기면 됩니다.
    public void StartFill(float fillDuration)
    {
        duration = fillDuration > 0f ? fillDuration : 0.05f;
        t = 0f;
        completed = false;
        isFilling = true;

        leftFill.fillAmount = 0f;
        rightFill.fillAmount = 0f;
    }

    // 진행 중인 예고를 중단합니다 (경직/사망 등으로 패턴이 취소될 때 호출).
    public void StopFill()
    {
        isFilling = false;
    }
}