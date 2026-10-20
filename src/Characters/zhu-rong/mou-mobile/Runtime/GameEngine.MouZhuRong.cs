namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouZhuRong => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiftMouZhuRongOutsideAssault);
    private int MouZhuRongTurnOrdinal => ProgramEventHistory<TurnStartedEvent>().Count();
    private static CardConversionSource MouZhuRongSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhu_rong_mou_mobile_MouZhuRong.MouZhuRongSource(f);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30400,
        SkillProgramEffectOp.RecordMouZhuRongLieren, SkillProgramEffectOp.SelectMouZhuRongLierenTarget, SkillProgramEffectOp.GiftMouZhuRongOutsideAssault)]
    private bool CanOfferMouZhuRong(ProgramTriggerCandidate candidate, SkillProgramTrigger t, ProgramSkillWindowContext c)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.GiftMouZhuRongOutsideAssault))
            return c.Window == SkillProgramTriggerWindow.TurnEnding && c.OwnerSeat == candidate.OwnerSeat &&
                !ProgramEventHistory<MouZhuRongAssaultUsedEvent>().Any(e => e.TurnOrdinal == MouZhuRongTurnOrdinal && e.ActorSeat == candidate.OwnerSeat);
        if (c.CardUse is not { } a || a.ActorSeat != candidate.OwnerSeat || !IsSlashCard(a.EffectiveKind)) return false;
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.RecordMouZhuRongLieren))
            return c.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
                LifecycleCardUse(a.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } action } use &&
                use.TargetSeats is [var target] && target != candidate.OwnerSeat && c.TargetSeat == target &&
                action.ActionId == a.CardActionId && !ProgramEventHistory<MouZhuRongLierenWonEvent>().Any(e => e.UseId == use.Id && e.Source.OwnerSeat == candidate.OwnerSeat);
        return c.Window == SkillProgramTriggerWindow.CardUseCompleted && MouZhuRongWin(candidate.OwnerSeat, c) is { } win &&
            !ProgramEventHistory<MouZhuRongLierenDamageChosenEvent>().Any(e => e.UseId == win.UseId && e.OwnerSeat == candidate.OwnerSeat) &&
            _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat && p.Seat != win.OpponentSeat);
    }
    [ProgramCapability] private sealed class MouZhuRongCapability(GameEngine engine) : IMouZhuRongHost, IMouZhuRongLierenTargets
    {
        public IReadOnlyList<int> OtherTargets(int owner) => engine.MouZhuRongLierenOtherTargets(owner);
        public void RecordLieren(ProgramSkillFrame f) => engine.RecordMouZhuRongLieren(f);
        public SkillProgramStepOutcome SelectLierenTarget(ProgramSkillFrame f) => engine.SelectMouZhuRongLierenTarget(f);
        public SkillProgramStepOutcome GiftAssault(ProgramSkillFrame f) => engine.GiftMouZhuRongOutsideAssault(f);
    }
}
