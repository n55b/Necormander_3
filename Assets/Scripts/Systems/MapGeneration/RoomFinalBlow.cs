using UnityEngine;

// A wave counts living fighters separately from their lingering death animations.
internal sealed class RoomFinalBlow
{
    private int living;
    private bool pending;
    private Vector3 lastContact;
    private Vector2 lastDirection;
    private bool bossFinish;
    internal bool Pending => pending;

    internal void Track(GameObject enemy)
    {
        if (enemy == null) return;
        var health = enemy.GetComponentInChildren<CharacterHealth>(true);
        if (health == null || health.IsDead || health.Encounter == this) return;
        health.Encounter?.Leave();
        health.Encounter = this;
        living++;
        pending = false;
    }

    internal void Leave()
    {
        living--;
        pending = false;
    }

    internal void Killed(Vector3 position, Vector2 direction, bool boss)
    {
        Leave();
        lastContact = position;
        lastDirection = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.up;
        bossFinish = boss;
        pending = living == 0;
    }

    internal void Resolve(bool finalWave)
    {
        if (!pending) return;
        pending = false;
        if (!finalWave || bossFinish) return;
        var settings = CameraSettingsSO.Instance;
        if (settings != null) HitStopManager.Instance?.DoSlowMotion(settings.finalBlowDuration, settings.finalBlowTimeScale);
        CameraManager.Instance?.Request(ImpactKind.RoomFinish, lastDirection);
        PolishFeedback.Play(FeedbackCue.LastEnemyImpact, lastContact, lastDirection, 3);
    }
}
