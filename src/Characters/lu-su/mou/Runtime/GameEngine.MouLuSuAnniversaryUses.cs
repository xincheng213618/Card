namespace CardGame.Core;
public sealed partial class GameEngine
{
    private ProgramSkillFrame? MouLuSuUseQuery(int actor)
    {
        var top = _resolutionStack.LastOrDefault(); var parent = top switch {
            DrawFundedDistinctBasicFrame p when IsMouLuSuPaymentFrame(p) => _resolutionStack.SingleOrDefault(f => f.Id == p.Payment.ParentFrameId),
            ChainedStateBasicFrame p when IsMouLuSuPaymentFrame(p) => _resolutionStack.SingleOrDefault(f => f.Id == p.Payment.ParentFrameId),
            ProgramSkillFrame { EquipmentSlotNamedUse: { } slot } when MouLuSuSlotParent(slot) is { } p => p, _ => top };
        return parent is ProgramSkillFrame { MouLuSuAnniversary: { Yin: false, Operation: SkillProgramEffectOp.ResolveMouLuSuMengmou, Stage: MouLuSuAnniversaryStage.Request or MouLuSuAnniversaryStage.DraftCards or MouLuSuAnniversaryStage.DraftTargets or MouLuSuAnniversaryStage.Issuing } r } f && r.Other == actor && r.Index < r.X && _players[actor].IsAlive && MouLuSuAnniversary.Valid(f) ? f : null;
    }
    private bool IsMouLuSuUseQuery(CharacterState actor) => MouLuSuUseQuery(actor.Seat) is not null;
    private static LegalAction FreezeMouLuSuAction(LegalAction a) => a with { TargetSeats = Array.AsReadOnly(a.TargetSeats.ToArray()), AdditionalConversionSources = Array.AsReadOnly((a.AdditionalConversionSources ?? []).ToArray()), SelectableCardIds = Array.AsReadOnly(a.SelectableCardIds.ToArray()), SelectableTargetSeats = Array.AsReadOnly(a.SelectableTargetSeats.ToArray()), SelectableCardTargets = a.SelectableCardTargets is null ? null : Array.AsReadOnly(a.SelectableCardTargets.ToArray()), SelectableTargetSequences = a.SelectableTargetSequences is null ? null : Array.AsReadOnly(a.SelectableTargetSequences.Select(x => (IReadOnlyList<int>)Array.AsReadOnly(x.ToArray())).ToArray()) };
    private IReadOnlyList<LegalAction> MouLuSuUseActions(ProgramSkillFrame f) => f.MouLuSuAnniversary is { } r && MouLuSuUseQuery(r.Other)?.Id == f.Id && _winner == Winner.None ? Array.AsReadOnly(BuildLegalActions(_players[r.Other],includeProgramActions:false).Where(a => a.Kind == LegalActionKind.Slash || a.Kind == LegalActionKind.UseEquipmentEffect && a.EquipmentKind == CardKind.ZhangbaSerpentSpear || a.PlayedCardKind is { } kind && IsSlashCard(kind) && a.Kind != LegalActionKind.UseProgramSkill).Select(FreezeMouLuSuAction).ToArray()) : [];
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit,36100)] private bool MouLuSuRequestedQuota(ProgramSlashQuotaQuery q) => IsMouLuSuUseQuery(q.Actor);
    private void IssueMouLuSuUse(ProgramSkillFrame f)
    {
        if (!MouLuSuAnniversary.Valid(f) || f.MouLuSuAnniversary is not { Stage: MouLuSuAnniversaryStage.Issuing, SelectedAction: { } action } r || MouLuSuUseQuery(r.Other)?.Id != f.Id) throw new InvalidOperationException("Mengmou lost its actual Yang Slash request.");
        if (action.DrawFundedDistinctBasicUse is not null) { BeginDrawFundedDistinctBasic(_players[r.Other],action.ConversionSource!,action.PlayedCardKind!.Value,action.TargetSeats,r.OriginalDecision!,new(DrawFundedDistinctBasicIntent.Play,f.Id,f.Id,null,f.InstructionIndex,action.TargetSeat)); return; }
        if (action.ChainedStateBasicUse is not null) { BeginChainedStateBasic(_players[r.Other],action.ConversionSource!,action.PlayedCardKind!.Value,action.TargetSeats,r.OriginalDecision!,new(ChainedStateBasicIntent.Play,f.Id,f.Id,null,f.InstructionIndex,action.TargetSeat)); return; }
        if (action.TieredRoundZeroUse is not null && action.ConversionSource is { } source && IsEquipmentSlotNamedSkill(source.SkillId)) { _pendingDecision = r.OriginalDecision; if (TryBeginEquipmentSlotNamedPlay(_players[r.Other],action)) return; ClearPendingDecision(); }
        ExecuteAction(_players[r.Other],action,r.Cards,r.Targets);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,36100)] private void BindMouLuSuUse(CardUseDeclaredEvent fact)
    {
        if (!HasMouLuSuAnniversary || LifecycleCardUse(fact.ResolutionId) is not { Action: { Type: CardActionType.Use } a } use) return;
        var i = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (i < 1 || _resolutionStack[i - 1] is not ProgramSkillFrame { MouLuSuAnniversary: { Stage: MouLuSuAnniversaryStage.Issuing, UseId: 0, ActionId: 0, SelectedAction: { } selected } r } f) return;
        var expected = selected.Kind == LegalActionKind.UseEquipmentEffect ? r.Cards : selected.CardId is > 0 ? [selected.CardId.Value] : Array.Empty<int>();
        if (!MouLuSuAnniversary.Valid(f) || !IsSlashCard(a.EffectiveKind) || a.ActorSeat != r.Other || a.ProviderSeat != r.Other || a.RequesterSeat is not null || use.SourceSeat != r.Other || !a.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(expected.Order()) || selected.PlayedCardKind is { } kind && a.EffectiveKind != kind || selected.ConversionSource is { } source && !a.ConversionChain.Contains(source)) throw new InvalidOperationException("Mengmou changed its selected actual Slash source or physical materials.");
        var origin = new MouLuSuAnniversaryUseOrigin(f.Id,r.Source,r.GameplayHash,r.Index,r.Other,use.Id,a.ActionId);
        ReplaceRuntimeFrame(f.Id,f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } });
        UpdateLifecycleCardUse(use.Id,u => u with { MouLuSuAnniversary = origin, UnlimitedUse = true }); AdvanceEventRulesAndQueueFact(new MouLuSuAnniversaryUseIssuedEvent(origin));
    }
    private bool MouLuSuNativeMatches(CardUseFrame use) => use.MouLuSuAnniversary is { } o && _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is ProgramSkillFrame { MouLuSuAnniversary: { Stage: MouLuSuAnniversaryStage.NativeUse } r } f && MouLuSuAnniversary.Valid(f) &&
        o.Source == r.Source && o.GameplayHash == r.GameplayHash && o.Index == r.Index && o.Actor == r.Other && o.UseId == use.Id && o.UseId == r.UseId && o.ActionId == r.ActionId && use.Action?.ActionId == o.ActionId && IsSlashCard(use.CardKind) && ShownEntityUseActorMatches(use,o.Actor,o.Actor) && ProgramEventHistory<MouLuSuAnniversaryUseIssuedEvent>().Count(e => e.Origin == o) == 1;
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit,36100)] private bool MouLuSuNoSlashDebit(long id,int actor) => LifecycleCardUse(id) is { MouLuSuAnniversary: { } o } use && actor == o.Actor && MouLuSuNativeMatches(use);
    [NativeUseValidation(36100)] private void ValidateMouLuSuUse(CardUseFrame use) { if (use.MouLuSuAnniversary is not null && (use.Step != ResolutionFrameStep.Completed || !MouLuSuNativeMatches(use))) throw new InvalidOperationException("Mengmou must return its actual completed native Slash."); }
    [NativeUseReturn(36100)] private void ReturnMouLuSuUse(CardUseFrame use)
    {
        if (use.MouLuSuAnniversary is not { } o) return; var f = GetActiveProgramFrame(o.FrameId); var r = f.MouLuSuAnniversary!;
        if (!MouLuSuNativeMatches(use) || r.Returned || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Mengmou cannot replace or repeat its native Slash return.");
        ReplaceRuntimeTop(f = f with { MouLuSuAnniversary = r with { Returned = true } }); if (use.CardAttack is not { Active: true }) SettleMouLuSuUse(f);
    }
    private void SettleMouLuSuUse(ProgramSkillFrame f)
    {
        var r = f.MouLuSuAnniversary!; if (!r.Returned || LifecycleCardUse(r.UseId) is not null) throw new InvalidOperationException("Mengmou must await its real whole-use tail.");
        ReplaceRuntimeTop(f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.UseChildren, Index = r.Index + 1, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat,0,null) }); AdvanceEventRulesAndQueueFact(new MouLuSuAnniversaryUseReturnedEvent(f.Id,r.Index,r.UseId,r.ActionId)); MouLuSuAnniversary.Continue(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup,36100)] private bool ReturnMouLuSuAttack() { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuSuAnniversary: { Stage: MouLuSuAnniversaryStage.NativeUse, Returned: true } } f) return false; SettleMouLuSuUse(f); return true; }
    private void AppendMouLuSuIssuedDamageCandidates(IDamageAttempt attack,SkillProgramTriggerWindow window,List<DamageTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.AfterDamageApplied || LifecycleCardUse(attack.ResolutionId) is not { MouLuSuAnniversary: { } origin } use || !MouLuSuNativeMatches(use) || attack.SourceSeat != origin.Actor || attack.IsSourceLess) return;
        var trigger = _contentRegistry.GetSkill(origin.Source.SkillId).Program!.Triggers.Single(t => t.Id == "requested-slash-damage" && t.Effects is [{ Op: SkillProgramEffectOp.RecoverMouLuSuMengmouDamage }]);
        result.RemoveAll(c => c.OwnerSeat == origin.Source.OwnerSeat && c.ProgramId == origin.Source.SkillId && c.ProgramTriggerId == trigger.Id);
        result.Add(new(origin.Source.OwnerSeat,$"program:{origin.Source.SkillId}:{trigger.Id}:{origin.Source.SkillInstanceId}:occurrence-0",trigger.Priority,false,origin.Source.SkillId,trigger.Id,origin.Source.SkillInstanceId,origin.GameplayHash,0));
    }
    private bool IsIssuedMouLuSuDamageCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context) => candidate.BindingId == "requested-slash-damage" && GetProgramTrigger(candidate).Effects is [{ Op: SkillProgramEffectOp.RecoverMouLuSuMengmouDamage }] && new MouLuSuAnniversaryFacts(this).Damage(candidate,context) is { } damage && context.DamageFrameId is { } id && LifecycleCardUse(damage.UseId) is { Action: { } action } && !ProgramEventHistory<MouLuSuAnniversaryDamageRewardIssuedEvent>().Any(e => e.DamageFrameId == id && e.UseId == damage.UseId && e.ActionId == action.ActionId);
}
