using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int MouLuZhiX(int owner, int target) => _players[owner].HasHpValue && _players[target].HasHpValue ? Math.Max(1, Math.Abs(_players[owner].Hp - _players[target].Hp)) : int.MaxValue;
    private int MouLuZhiPhaseUses(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouLuZhiStartedEvent>().Count(e =>
        e.Source.OwnerSeat == owner && e.Op == SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang && e.Phase == phase);
    [ProgramPhaseUsedProjection(70)]
    private int MouLuZhiProjectedUses(int owner, string skill, int fallback) => skill == MouLuZhiZhenliang && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase
        ? MouLuZhiPhaseUses(owner, phase) : fallback;
    private int[] MouLuZhiYangTargets(int owner, MouLuZhiRen ren, string instance) => GetProgramTargetSeats(owner, SkillProgramTargetKind.OtherLivingInAttackRange).Where(target =>
        MouLuZhiCostCards(owner, CapturePhysicalCardColor(owner, GetAdvancedCard(ren.CardId)), instance).Length >= MouLuZhiX(owner, target)).ToArray();
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 31000, SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang)]
    private bool CanStartMouLuZhiYang(CharacterState owner) => GetProgramConversionPolarity(owner.Seat, MouLuZhiZhenliang) == SkillPolarity.Yang &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == owner.Seat && IsRecordedActualDiscardRecoveryPhase(phase) &&
        MouLuZhiPhaseUses(owner.Seat, phase) == 0 && GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == MouLuZhiZhenliang).Any(i =>
            MatchingMouLuZhiRen(owner.Seat, i.SkillInstanceId) is { } ren && MouLuZhiYangTargets(owner.Seat, ren, i.SkillInstanceId).Length > 0);
    private bool MouLuZhiOutsideTurn(int owner) => HasOngoingWholeActualUseTurn && _currentSeat != owner;
    private bool MouLuZhiCompletion(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.CardUse is not { } card || _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w ||
            w.Action.ActionId != card.CardActionId || GetCardActionWindow(w) != context.Window || !MouLuZhiOutsideTurn(c.OwnerSeat) ||
            GetProgramConversionPolarity(c.OwnerSeat, c.SkillId) != SkillPolarity.Yin || MatchingMouLuZhiRen(c.OwnerSeat, c.SkillInstanceId) is not { } ren ||
            GetProgramCardCategory(w.Action.EffectiveKind) != GetProgramCardCategory(GetAdvancedCard(ren.CardId).Kind) ||
            ProgramEventHistory<MouLuZhiStartedEvent>().Any(e => e.Op == SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin && e.Source.OwnerSeat == c.OwnerSeat && e.ActionId == card.CardActionId)) return false;
        return context.Window == SkillProgramTriggerWindow.CardUseCompleted
            ? w.Action.Type == CardActionType.Use || w.CompletedResponseReturn is { IsCommitted: false } && IsCompletedResponseParentConsistent(w)
            : context.Window == SkillProgramTriggerWindow.CardResponseCompleted && w.ResponseCompletion is { CostsDrained: true } && ValidResponseCompletionWindow(w) &&
              !IsMouLuZhiTrueResponseUse(w.Action);
    }
    private bool IsMouLuZhiTrueResponseUse(CardActionContext action) => IsCompletedResponseUse(action, ProgramCompletedResponseKind.Nullification) ||
        IsCompletedResponseUse(action, ProgramCompletedResponseKind.Dodge) && _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ParentActionId)
            is { CardAttack: { EffectiveCardKind: { } kind } attack } && IsSlashCard(kind) && attack.TargetSeat == action.ActorSeat;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 31000, SkillProgramEffectOp.ExchangeMouLuZhiMingren, SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin)]
    private bool CanOfferMouLuZhi(ProgramTriggerLegalityContext gate) => gate.Candidate.SkillId == MouLuZhiMingren
        ? MatchingMouLuZhiRen(gate.Owner.Seat, gate.Candidate.SkillInstanceId) is not null && GetHand(gate.Owner).Count > 0
        : MouLuZhiCompletion(gate.Candidate, gate.Context);
}
