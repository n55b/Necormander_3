
// ---------------------------------------------------------
// 더미 핸들러 (다른 매니저에서 효과를 직접 처리하는 보석들용)
// ---------------------------------------------------------
public class EmptyGemHandler : IGemEffectHandler
{
    public GemUniqueType HandledType { get; }
    public EmptyGemHandler(GemUniqueType type) { HandledType = type; }
    public void OnEquipped() { }
    public void OnUnequipped() { }
}
