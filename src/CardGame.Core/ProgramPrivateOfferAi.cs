namespace CardGame.Core;

internal sealed partial class ProgramAiEstimateContext
{
    internal void PriceGameTargetHandHpChoice()
    {
        if (_publicContext.SelectedTarget is not { } target) { _targetDraw += 2; return; }
        if (target.HandCount < target.Hp)
        { if (target.Seat == _player.Seat) _ownerDraw += 2; else _targetDraw += 2; }
        else if (target.HandCount > target.Hp)
        { if (target.Seat == _player.Seat) _otherAdjustment -= 16; else _targetAdjustment -= 16; }
    }
}
