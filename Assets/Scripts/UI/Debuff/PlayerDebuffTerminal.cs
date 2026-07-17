/// <summary>
/// 플레이어 상태이상 UI. 아직 표시할 자리를 안 잡아서 전부 빈 구현이다.
/// (플레이어 CC 는 Phase 5 에서 들어온다 — 그때 여기도 채워야 한다.)
/// </summary>
public class PlayerDebuffTerminal : Base_DebuffUITerminal
{
    public override void UpdateUI(StatusType type, float value) { }
    public override void RemoveIcon(StatusType type) { }
    public override void RemoveAll() { }
}
