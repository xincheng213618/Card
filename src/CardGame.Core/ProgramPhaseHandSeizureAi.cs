namespace CardGame.Core;

internal sealed partial class ProgramAiEstimateContext
{
    internal void PricePhaseHandSeizure()
    {
        if (_publicContext.SelectedTarget is not { } target) return;
        // Public counts only. The acquired cards and the target's printed identities are never consulted.
        var acquired = target.Seat == _player.Seat ? 0 : target.HandCount;
        _ownerDraw += acquired;
        _targetAdjustment -= acquired * 4;
        _discardsSelected = true;
        _estimatedHandCount = Math.Max(0, _estimatedHandCount - 1) + acquired;
        _otherAdjustment += _faceDown ? 12 : -12;
        _faceDown = !_faceDown;
        if (target.Seat != _player.Seat) _otherAdjustment -= Math.Max(0, target.Hp) * 2;
    }
}
