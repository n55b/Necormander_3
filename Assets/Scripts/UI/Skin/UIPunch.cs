using UnityEngine;

/// <summary>
/// '툭 튀기'(크기가 잠깐 커졌다 돌아오는 연출) 공용 처리. 모양은 UI 스킨 설정의 '툭 튀는 모양 곡선'을 따른다.
/// UIBarMotion · UISkillSlotMotion 이 같이 쓴다.
/// </summary>
public class UIPunch
{
    private RectTransform _target;
    private Vector3 _baseScale;
    private float _time = -1f, _duration, _amount;

    public bool IsPlaying => _time >= 0f;

    /// <summary>움직일 대상을 정한다. 처음 한 번의 크기를 '원래 크기'로 기억한다.</summary>
    public void Bind(RectTransform target)
    {
        if (_target == target || target == null) return;
        _target = target;
        _baseScale = target.localScale;
    }

    public void Play(float amount, float duration)
    {
        if (_target == null || duration <= 0f || amount <= 1.0001f) return;
        _amount = amount; _duration = duration; _time = 0f;
    }

    public void Tick(float dt)
    {
        if (_time < 0f || _target == null) return;
        _time += dt;
        float k = Mathf.Clamp01(_time / _duration);
        _target.localScale = _baseScale * Mathf.Lerp(1f, _amount, UISkinSettings.EvaluatePunch(k));
        if (k >= 1f) Stop();
    }

    public void Stop()
    {
        _time = -1f;
        if (_target != null) _target.localScale = _baseScale;
    }
}
