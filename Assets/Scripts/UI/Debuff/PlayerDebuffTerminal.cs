/// <summary>
/// 플레이어의 상태이상 UI 연결점. 현재 이 터미널은 아이콘을 표시하지 않는다.
/// </summary>
public class PlayerDebuffTerminal : Base_DebuffUITerminal
{
    public override void UpdateUI(StatusType type, float value) { }
    public override void RemoveIcon(StatusType type) { }
    public override void RemoveAll() { }
}
