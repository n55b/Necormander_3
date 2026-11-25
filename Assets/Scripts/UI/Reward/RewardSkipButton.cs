using UnityEngine;
using UnityEngine.UI;

public sealed class RewardSkipButton : Button
{
    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        if (targetGraphic is RewardSkipSurface plate)
        {
            bool focus = state == SelectionState.Highlighted || state == SelectionState.Selected;
            plate.SetAppearance(focus || state == SelectionState.Pressed, state == SelectionState.Pressed,
                state == SelectionState.Disabled, instant);
        }
    }
}
