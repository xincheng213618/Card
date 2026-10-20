namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26810)]
    private void RequireCaoXi(ProgramSkillFrame f)
    {
        if (f.CaoXiGangshu is not null && !ValidCaoXiGangshu(f) || f.CaoXiJianxuan is not null && !ValidCaoXiJianxuan(f))
            throw new InvalidOperationException("Cao Xi lost its original source, actual use/damage window, single recipient or paid per-draw cursor.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && (f.CaoXiGangshu is not null || f.CaoXiJianxuan?.Stage == CaoXiJianxuanStage.ChoosingRecipient) &&
            (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, CaoXiChoices(f))))
            throw new InvalidOperationException("Cao Xi lost its exact unissued owner choice.");
    }
}
