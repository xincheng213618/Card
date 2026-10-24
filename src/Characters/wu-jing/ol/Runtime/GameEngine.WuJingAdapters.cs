namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool WuJingRequestIdentity(ProgramSkillFrame f) => f.WuJing is { Operation: SkillProgramEffectOp.UseHejiAtCompletedSingleTarget } r && f.InstructionIndex == 1 && r.Source == WuJingSource(f) && r.GameplayHash == f.GameplayHash && r.ActualTurn == _turnNumber &&
        ProgramEventHistory<WuJingStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Operation == r.Operation && e.WindowId == r.WindowId && e.UseId == r.UseId && e.ActionId == r.ActionId && e.TargetSeat == r.TargetSeat) == 1;
    private bool IsWuJingHejiRequest(ProgramSkillFrame f) => WuJingRequestIdentity(f) && f.WuJing!.Stage == WuJingStage.UseChoice;
    private bool HasWuJingZeroSupply(ProgramSkillFrame f)
    {
        if (!IsWuJingHejiRequest(f) || !_players[f.OwnerSeat].IsAlive || !_players[f.WuJing!.TargetSeat].IsAlive) return false;
        var actor = _players[f.OwnerSeat]; var target = _players[f.WuJing.TargetSeat];
        return HasBoundaryLiuFengProgramSlash(actor, [target.Seat]) || HasWeijieProgramSlash(actor, [target.Seat]) || (HasZonghuProgramSlash(actor, [target.Seat]) || HasHuanpeiProgramSlash(actor, [target.Seat])) || DrawFundedDistinctBasicRules(actor).Select(r => (r.Source, r.Rule.OutputKind)).Concat(ChainedStateBasicRules(actor).Select(r => (r.Source, r.Rule.OutputKind))).Any(r => IsSlashCard(r.OutputKind) &&
            !HasBeneficiarySuitShield(actor.Seat, target.Seat, Suit.None) && !IsCardTargetProhibited(target, r.OutputKind, Suit.None) && !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, r.OutputKind) &&
            CanUseSlashTarget(actor, target, new(0, r.OutputKind, Suit.None, 0), r.Source, r.OutputKind, ignoreDistance: true, noEffectiveRank: true, physicalCardIds: []));
    }
    private bool WuJingHasPaidAdapter(ProgramSkillFrame f) => WuJingRequestIdentity(f) &&
        (HasWeijieSpecializedPayment(f) || (HasZonghuSpecializedPayment(f) || HasHuanpeiProgramPayment(f)) || f.WuJing!.RoundOncePayment is not null || f.WuJing.DrawFundedPayment is not null || f.WuJing.ChainedPayment is not null ||
         _resolutionStack.OfType<DrawFundedDistinctBasicFrame>().Any(p => p.Payment.ParentFrameId == f.Id && p.Payment.RequestFrameId == f.Id && p.Payment.Cursor == f.InstructionIndex && p.Payment.Source.OwnerSeat == f.OwnerSeat && p.Payment.TargetSeat == f.WuJing.TargetSeat && p.OriginalDecision.SkillPrompt?.SkillId == f.SkillId && ValidDrawFundedDistinctBasicPayment(p.Payment)) ||
         _resolutionStack.OfType<ChainedStateBasicFrame>().Any(p => p.Payment.ParentFrameId == f.Id && p.Payment.RequestFrameId == f.Id && p.Payment.Cursor == f.InstructionIndex && p.Payment.Source.OwnerSeat == f.OwnerSeat && p.Payment.TargetSeat == f.WuJing.TargetSeat && p.OriginalDecision.SkillPrompt?.SkillId == f.SkillId && ValidChainedStateBasicPayment(p.Payment)));
    private bool WuJingHejiAdapterActor(ProgramSkillFrame f, int actor, int? target, int cursor, long? parent, long? request) => WuJingRequestIdentity(f) && actor == f.OwnerSeat && target == f.WuJing!.TargetSeat && cursor == f.InstructionIndex && parent == f.Id && request == f.Id &&
        f.WuJing.Stage is WuJingStage.UseChoice or WuJingStage.IssuingUse or WuJingStage.NativeUse && (HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || WuJingHasPaidAdapter(f));
    private void PrepareWuJingDrawFundedSlash(ProgramSkillFrame f, DrawFundedDistinctBasicPayment p)
    {
        if (!IsWuJingHejiRequest(f) || !ValidDrawFundedDistinctBasicPayment(p) || p.Intent != DrawFundedDistinctBasicIntent.AssistedSlash || p.ParentFrameId != f.Id || p.RequestFrameId != f.Id || p.Cursor != f.InstructionIndex || p.Source.OwnerSeat != f.OwnerSeat || p.TargetSeat != f.WuJing!.TargetSeat || !IsSlashCard(p.EffectiveKind)) throw new InvalidOperationException("Heji draw-funded Slash lost its real once-paid draw, original request or same target.");
        ReplaceRuntimeTop(f with { WuJing = f.WuJing with { Stage = WuJingStage.IssuingUse, DrawFundedPayment = p, Selection = new(p.EffectiveKind, false, false) { Conversions = [p.Source] }, Before = _movementSequence, After = _movementSequence } });
    }
    private void PrepareWuJingChainedSlash(ProgramSkillFrame f, ChainedStateBasicPayment p)
    {
        if (!IsWuJingHejiRequest(f) || !ValidChainedStateBasicPayment(p) || p.Intent != ChainedStateBasicIntent.AssistedSlash || p.ParentFrameId != f.Id || p.RequestFrameId != f.Id || p.Cursor != f.InstructionIndex || p.Source.OwnerSeat != f.OwnerSeat || p.TargetSeat != f.WuJing!.TargetSeat || !IsSlashCard(p.EffectiveKind)) throw new InvalidOperationException("Heji chained Slash lost its genuine paid state change, request or same target.");
        ReplaceRuntimeTop(f with { WuJing = f.WuJing with { Stage = WuJingStage.IssuingUse, ChainedPayment = p, Selection = new(p.EffectiveKind, false, false) { Conversions = [p.Source] }, Before = _movementSequence, After = _movementSequence } });
    }
    private bool FinishWuJingPaidAdapter(ProgramSkillFrame f) { if (!IsWuJingHejiRequest(f)) return false; FinishWuJing(f, false); return true; }
    private void PrepareWuJingKenshangNative(ProgramSkillFrame f, ProgramMultiCardViewAsSelection selection)
    {
        if (!IsWuJingHejiRequest(f)) return;
        if (selection.Cards.Count < 2 || ViewAsRule(selection.Source)?.KenshangUse != true || !WuJingCanUse(f.OwnerSeat, f.WuJing!.TargetSeat, selection.Cards[0], CardKind.Slash, selection.Source, selection.Cards.Select(c => c.Id).ToArray()) ||
            ProgramEventHistory<KenshangRequestFinishedEvent>().Count(e => e.OwnerFrameId == f.Id && e.ActorSeat == f.OwnerSeat && e.Accepted && e.MaterialCount == selection.Cards.Count) != 1) throw new InvalidOperationException("Heji Kenshang must finish its own actual private HE material request.");
        ReplaceRuntimeTop(f with { WuJing = f.WuJing with { Stage = WuJingStage.IssuingUse, Selection = new(CardKind.Slash, false, false) { CardIds = selection.Cards.Select(c => c.Id).ToArray(), Conversions = [selection.Source] }, Before = _movementSequence, After = _movementSequence } });
    }
    private bool WuJingZeroPaymentProof(ProgramSkillFrame f, WuJingReceipt r, CardUseFrame? use = null)
    {
        if (HasWeijieSpecializedPayment(f, use) || (HasZonghuSpecializedPayment(f, use) || HasHuanpeiProgramPayment(f, use))) return true;
        if (r.Selection is not { CardIds.Count: 0, Conversions: [var source] } || source.OwnerSeat != f.OwnerSeat ||
            new[] { r.RoundOncePayment is not null, r.DrawFundedPayment is not null, r.ChainedPayment is not null }.Count(v => v) != 1) return false;
        if (r.RoundOncePayment is { } quota) return quota.Source == source && quota.ActualTurnNumber == r.ActualTurn && quota.ActualTurnOwnerSeat == _turnProgression.OwnerSeat && quota.GameplayHash == _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash &&
            ProgramEventHistory<RoundOnceCardlessBasicQuotaPaidEvent>().Count(e => e.Receipt == quota) == 1 && (use is null || IsRoundOnceCardlessBasicUse(use.Id));
        if (r.DrawFundedPayment is { } draw) return draw.Source == source && draw.ParentFrameId == f.Id && draw.RequestFrameId == f.Id && draw.TargetSeat == r.TargetSeat && draw.Cursor == f.InstructionIndex && ValidDrawFundedDistinctBasicPayment(draw) && (use is null || use.DrawFundedDistinctBasicUse?.Payment == draw);
        return r.ChainedPayment is { } chain && chain.Source == source && chain.ParentFrameId == f.Id && chain.RequestFrameId == f.Id && chain.TargetSeat == r.TargetSeat && chain.Cursor == f.InstructionIndex && ValidChainedStateBasicPayment(chain) && (use is null || use.ChainedStateBasicUse?.Payment == chain);
    }
    private void BeginWuJingRoundOnce(ProgramSkillFrame f, CardConversionSource source)
    {
        if (!IsWuJingHejiRequest(f) || !RoundOnceCardlessRules(_players[f.OwnerSeat], CardKind.Slash).Any(entry => entry.Source == source) || !RoundOnceCardlessProgramTargetLegal(_players[f.OwnerSeat], f.WuJing!.TargetSeat, source, true)) throw new InvalidOperationException("Heji cardless Slash lost its own available independent round quota.");
        var paid = ConsumeRoundOnceCardlessBasic(source, false); var r = f.WuJing!;
        ReplaceRuntimeTop(f = f with { WuJing = r with { Stage = WuJingStage.IssuingUse, RoundOncePayment = paid, Selection = new(CardKind.Slash, false, false) { Conversions = [source] }, Before = _movementSequence, After = _movementSequence } });
        var actor = _players[f.OwnerSeat]; var target = _players[r.TargetSeat]; var id = BeginCardUse(new(0, CardKind.Slash, Suit.None, 0), actor.Seat, [target.Seat], CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), physicalCardIds: [], conversionSource: source); IssueRoundOnceCardlessNativeUse(id, source);
        var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, target.Seat, IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, target);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, LifecycleCardUse(id)!.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private bool IsWuJingHejiZeroUse(CardUseFrame use) => use.WuJingHeji is { Origin.Materials.Count: 0 } && IsWuJingHejiUse(use);
}
