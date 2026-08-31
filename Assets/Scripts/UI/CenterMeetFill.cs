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
        if (leftFill == null || rightFill == null) return; // 배선이 비면 매 프레임 NRE 가 난다.
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

        if (leftFill != null) leftFill.fillAmount = 0f;
        if (rightFill != null) rightFill.fillAmount = 0f;
    }

    // 게이지 색. 카운터 신호로 쓴다 — 노랑=진짜(때리면 패턴 취소) / 빨강=페이크(때리면 즉시 시전)
    // / 무채색=카운터 불가. 프리팹에 박힌 색을 덮어쓰므로 Begin 할 때마다 반드시 다시 정해야 한다.
    public void SetColor(Color c)
    {
        if (leftFill != null) leftFill.color = c;
        if (rightFill != null) rightFill.color = c;
    }

    // 진행 중인 예고를 중단합니다 (경직/사망 등으로 패턴이 취소될 때 호출).
    public void StopFill()
    {
        isFilling = false;
    }
}