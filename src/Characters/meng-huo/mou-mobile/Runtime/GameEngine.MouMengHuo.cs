namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasMouMengHuoCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouMengHuoZaiqi);
    private static CardConversionSource MouMhSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_meng_huo_mou_mobile_MouMengHuo.MouMhSource(f);
    private int MouMhCharge(int owner) => _players[owner].Markers.GetValueOrDefault(PlayerMarkerKind.MouMengHuoCharge);
    private MouMengHuoTurnKey? MouMhTurn()
    {
        var h = CompleteProgramEventHistory().ToArray();
        return h.LastOrDefault(e => e is TurnStartedEvent or ActualTurnCursorReturnedEvent) is TurnStartedEvent t && t.TurnNumber == _turnNumber && t.ActorSeat == _turnProgression.OwnerSeat
            ? new(t.TurnNumber, t.ActorSeat, h.OfType<TurnStartedEvent>().Count()) : null;
    }
    private void ChangeMouMhCharge(ProgramSkillFrame f, int delta)
    {
        var before = MouMhCharge(f.OwnerSeat); var count = checked(before + delta);
        if (count is < 0 or > 7) throw new InvalidOperationException("Zaiqi's actual charge must remain between zero and seven.");
        var owner = _players[f.OwnerSeat]; var key = (PlayerMarkerKind.MouMengHuoCharge, f.OwnerSeat);
        if (count == 0) { owner.Markers.Remove(PlayerMarkerKind.MouMengHuoCharge); owner.MarkerSourceCounts.Remove(key); }
        else { owner.Markers[PlayerMarkerKind.MouMengHuoCharge] = count; owner.MarkerSourceCounts[key] = count; }
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, f.OwnerSeat, PlayerMarkerKind.MouMengHuoCharge, delta, count, f.OwnerSeat, f.SkillId));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 30900)]
    private void CaptureMouMhUse(CardUseDeclaredEvent e)
    {
        if (!HasMouMengHuoCapability || LifecycleCardUse(e.ResolutionId) is not { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.BarbarianAssault } a } ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != a.ActorSeat ||
            ProgramEventHistory<MouMengHuoBarbarianUsedEvent>().Any(x => x.UseId == e.ResolutionId)) return;
        AdvanceEventRulesAndQueueFact(new MouMengHuoBarbarianUsedEvent(a.ActorSeat, phase, e.ResolutionId, a.ActionId));
    }
    private bool IsMouMengHuoUseForbidden(int actor, CardKind kind, CardActionType type, long? existingUseId)
    {
        if (!HasMouMengHuoCapability || kind != CardKind.BarbarianAssault || type != CardActionType.Use || !HasRuntimeSkill(_players[actor], MouMengHuoComposition.Huoshou) ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != actor) return false;
        if (existingUseId is { } id && LifecycleCardUse(id) is { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.BarbarianAssault } a } && a.ActorSeat == actor &&
            ProgramEventHistory<MouMengHuoBarbarianUsedEvent>().Any(e => e.UseId == id && e.ActionId == a.ActionId && e.ActorSeat == actor && e.Phase == phase)) return false;
        return ProgramEventHistory<MouMengHuoBarbarianUsedEvent>().Any(e => e.ActorSeat == actor && e.Phase == phase);
    }
    private bool RestoreMouMengHuoDamageSource(GroupCardHandle group)
    {
        if (!HasMouMengHuoCapability || LifecycleCardUse(group.ResolutionId) is not { CardKind: CardKind.BarbarianAssault, Action: { } a, MouMengHuoDamageSource: { } r } use ||
            r.UseId != use.Id || r.ActionId != a.ActionId || r.OriginalActorSeat != a.ActorSeat ||
            !ProgramEventHistory<MouMengHuoAttributionIssuedEvent>().Any(e => e.Attribution == r)) return false;
        group.DamageSourceSeat = r.Source.OwnerSeat;
        AdvanceEventRulesAndQueueFact(new HuoshouAttributedEvent(use.Id, a.ActorSeat, r.Source.OwnerSeat)); return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30900, SkillProgramEffectOp.ResolveMouMengHuoAttribution,
        SkillProgramEffectOp.ObtainMouMengHuoBarbarian, SkillProgramEffectOp.GainMouMengHuoCharge, SkillProgramEffectOp.ResolveMouMengHuoZaiqi)]
    private bool CanOfferMouMh(ProgramTriggerLegalityContext g)
    {
        if (g.Features.HasOperation(SkillProgramEffectOp.ResolveMouMengHuoAttribution))
            return g.Context.CardUse is { } c && c.ActorSeat != g.OwnerSeat && LifecycleCardUse(c.ParentCardUseFrameId) is { CardKind: CardKind.BarbarianAssault, MouMengHuoDamageSource: null };
        if (g.Features.HasOperation(SkillProgramEffectOp.ObtainMouMengHuoBarbarian))
            return CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == g.OwnerSeat && _cardZones.CardsAt(CardLocation.DiscardPile).Any(c => c.Kind == CardKind.BarbarianAssault);
        if (g.Features.HasOperation(SkillProgramEffectOp.GainMouMengHuoCharge))
            return g.Context.SourceSeat == g.OwnerSeat && g.Context.Amount > 0 && MouMhCharge(g.OwnerSeat) < 7 && MouMhTurn() is { } turn &&
                !ProgramEventHistory<MouMengHuoChargeGainedEvent>().Any(e => e.Source.OwnerSeat == g.OwnerSeat && e.Turn == turn);
        return MouMhCharge(g.OwnerSeat) > 0 && g.Context.Window == SkillProgramTriggerWindow.DiscardPhaseEnded && g.Context.SourceSeat == g.OwnerSeat;
    }
    [ProgramCapability] private sealed class MouMengHuoCapability(GameEngine g) : IMouMengHuoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => g.ResolveMouMh(f, op); }
    private SkillProgramStepOutcome ResolveMouMh(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id); var c = f.WindowContext;
        if (f.InstructionIndex != 1 || c is null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) throw new InvalidOperationException("Mou Meng Huo requires its original native trigger frame.");
        if (op == SkillProgramEffectOp.ResolveMouMengHuoAttribution)
        {
            if (c.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || c.CardUse is not { } card || LifecycleCardUse(card.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.BarbarianAssault } action, MouMengHuoDamageSource: null } use || action.ActorSeat == f.OwnerSeat)
                throw new InvalidOperationException("Huoshou must freeze the actual other actor's native finalized Barbarian Use.");
            var r = new MouMengHuoAttribution(MouMhSource(f), f.GameplayHash, use.Id, action.ActionId, action.ActorSeat);
            ReplaceRuntimeFrame(use.Id, use with { MouMengHuoDamageSource = r }); AdvanceEventRulesAndQueueFact(new MouMengHuoAttributionIssuedEvent(f.Id, r)); return SkillProgramStepOutcome.Continue;
        }
        if (op == SkillProgramEffectOp.GainMouMengHuoCharge)
        {
            if (c.Window != SkillProgramTriggerWindow.AfterDamageApplied || c.SourceSeat != f.OwnerSeat || c.Amount <= 0 || MouMhTurn() is not { } turn || c.DamageFrameId is not { } damage ||
                MouMhCharge(f.OwnerSeat) >= 7 || ProgramEventHistory<MouMengHuoChargeGainedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Turn == turn)) return SkillProgramStepOutcome.Continue;
            var before = MouMhCharge(f.OwnerSeat); ChangeMouMhCharge(f, 1);
            AdvanceEventRulesAndQueueFact(new MouMengHuoChargeGainedEvent(f.Id, MouMhSource(f), turn, damage, before, before + 1)); return SkillProgramStepOutcome.Continue;
        }
        var obtain = op == SkillProgramEffectOp.ObtainMouMengHuoBarbarian;
        if (c.Window != (obtain ? SkillProgramTriggerWindow.PlayPhaseStarting : SkillProgramTriggerWindow.DiscardPhaseEnded) || c.SourceSeat != f.OwnerSeat || f.MouMengHuo is not null)
            throw new InvalidOperationException("Mou Meng Huo lost its own exact phase boundary.");
        var state = new MouMengHuoReceipt(MouMhSource(f), f.GameplayHash, c.ParentFrameId, obtain, obtain ? 0 : MouMhCharge(f.OwnerSeat))
        { Stage = obtain ? MouMengHuoStage.ObtainChildren : MouMengHuoStage.Selecting, Before = _movementSequence, After = _movementSequence, Accepted = obtain };
        ReplaceRuntimeTop(f = f with { MouMengHuo = state }); AdvanceEventRulesAndQueueFact(new MouMengHuoStartedEvent(f.Id, state.Source, state.GameplayHash, state.WindowId, obtain, state.Budget));
        if (!obtain) { PublishMouMh(f); return SkillProgramStepOutcome.AwaitChoice; }
        var candidates = _cardZones.CardsAt(CardLocation.DiscardPile).Where(k => k.Kind == CardKind.BarbarianAssault).ToArray();
        if (candidates.Length > 0)
        {
            var entity = candidates[_random.Next(candidates.Length)]; var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(entity, CardLocation.DiscardPile, CardLocation.Hand(f.OwnerSeat), new("skill-program.mou-mobile:huoshou.obtain"),
                move => AdvanceEventRulesAndQueueFact(new MouMengHuoObtainEvent(f.Id, entity.Id, move.From, move.To, move.Sequence)));
            SealMouMh(f.Id, before);
        }
        ContinueMouMh(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 70)]
    private bool MouMengHuoCardUseForbidden(ProgramCardUseQuery query) =>
        IsMouMengHuoUseForbidden(query.ActorSeat, query.Kind, query.ActionType, query.ExistingUseFrameId);
}
