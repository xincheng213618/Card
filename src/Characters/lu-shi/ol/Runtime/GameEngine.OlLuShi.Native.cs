namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int[] OlLuShiLeijieSlashTargets(ProgramSkillFrame f) => CurrentTableOrder().Select(seat => _players[seat]).Where(p => CanUseVirtualSlashTarget(_players[f.OwnerSeat], p, CardKind.ThunderSlash,
        physicalSuit: Suit.None, effectiveRank: 0, ignoreDistance: true, countsTowardLimit: false) && !IsDirectedCardTargetProhibited(f.OwnerSeat, p.Seat, CardKind.ThunderSlash) && !IsCardTargetProhibited(p, CardKind.ThunderSlash, Suit.None, null)).Select(p => p.Seat).ToArray();
    private bool CanOlLuShiLeijieSlash(ProgramSkillFrame f, int targetSeat) => OlLuShiLeijieSlashTargets(f).Contains(targetSeat);

    // Same native pipeline as an offered zero-material Slash, retaining the exact
    // ThunderSlash appearance and returning each whole use before the next.
    private void IssueOlLuShiLeijieSlash(ProgramSkillFrame f)
    {
        var r = f.OlLuShi!; var targetSeat = r.TargetSeat;
        if (r.Stage != OlLuShiStage.Ready || r.SlashCount >= 2 || !OlLuShiLeijieSlashTargets(f).Contains(targetSeat) || ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("Leijie cannot issue an overlapping or illegal native thunder Slash.");
        ReplaceRuntimeTop(f = f with { OlLuShi = r with { Stage = OlLuShiStage.NativeUse, SlashCount = r.SlashCount + 1, TargetSeat = targetSeat, NativeReturned = false } });
        var source = _players[f.OwnerSeat]; var target = _players[targetSeat]; var id = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, f.OwnerSeat, f.OwnerSeat, null, null, null, CardKind.ThunderSlash, [targetSeat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        var origin = new OlLuShiSlashOrigin(f.Id, r.Source, r.Hash, r.SlashCount + 1, id, action.ActionId, targetSeat);
        ReplaceRuntimeTop(f with { OlLuShi = f.OlLuShi! with { Stage = OlLuShiStage.NativeUse, UseId = id, ActionId = action.ActionId } });
        PushRuntimeFrame(new CardUseFrame(id, f.OwnerSeat, 0, CardKind.ThunderSlash, [targetSeat], PhysicalCardIds: []) { Action = action, UnlimitedUse = true, OlLuShiLeijie = origin });
        AdvanceEventRulesAndQueueFact(new OlLuShiSlashIssuedEvent(origin));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, CardKind.ThunderSlash, f.OwnerSeat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, [targetSeat]));
        RecordYingboCardUse(id, f.OwnerSeat, CardKind.ThunderSlash);
        RecordProgramUsedBasicCard(f.OwnerSeat, CardKind.ThunderSlash);
        RecordActualPlayPhaseUse(action);
        var attack = new CardAttackHandle(this, id, f.OwnerSeat, targetSeat, card: null,
            damageAmount: 1 + source.AlcoholDamageBonus, playedCardKind: CardKind.ThunderSlash,
            ignoresArmor: HasCardArmorBypass(source, target, CardKind.ThunderSlash), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(id, source); source.HasAlcoholEffect = false; ActiveCardAttack = attack;
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(source.Seat, CardKind.ThunderSlash);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.ThunderSlash, f.OwnerSeat, targetSeat)); TryMarkProgramUseCommitted(id);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private bool IsOlLuShiLeijieNativeSlash(CardUseFrame use)
    {
        if (use.OlLuShiLeijie is not { } o || use.CardId != 0 || use.CardKind != CardKind.ThunderSlash || use.UnlimitedUse != true ||
            use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.ThunderSlash, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a ||
            use.Id != o.UseId || a.ActionId != o.ActionId || a.ActorSeat != o.Source.OwnerSeat || a.ProviderSeat != o.Source.OwnerSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is not ProgramSkillFrame { OlLuShi: { Operation: SkillProgramEffectOp.ResolveOlLuShiLeijie, Stage: OlLuShiStage.NativeUse } r } f ||
            r.Source != o.Source || r.Hash != o.Hash || r.SlashCount != o.Ordinal || r.TargetSeat != o.TargetSeat || r.UseId != use.Id || r.ActionId != a.ActionId ||
            ProgramEventHistory<OlLuShiSlashIssuedEvent>().Count(e => e.Origin == o) != 1 || !HasOlLuShiJudgmentProof(f)) return false;
        return ShownEntityUseActorMatches(use, o.Source.OwnerSeat, o.Source.OwnerSeat);
    }
    private bool IsOlLuShiLeijieAttackConsistent(CardAttackHandle a, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(a.ResolutionId) is not { OlLuShiLeijie: { } o } use || !IsOlLuShiLeijieNativeSlash(use) ||
            a.Card is not null || a.PhysicalCards.Count != 0 || processing.Count != 0 || a.EffectiveCardKind != CardKind.ThunderSlash ||
            a.ProgramSkillCardUseFrameId != o.FrameId || a.ProgramSkillFrameId is not null || a.SourceSeat != use.SourceSeat || a.CardUserSeat != use.SourceSeat || a.IsSourceLess)
            return false;
        if (!a.IsChainPropagation) return use.TargetSeats.Contains(a.TargetSeat) || a.DamageRedirected;
        // Chain propagation is the same native attack's frozen recipient cursor;
        // do not create a program-damage parent or a second virtual-card origin.
        return a.ChainedTargetIndex > 0 && a.ChainedTargetIndex <= a.ChainedTargetSeats.Count &&
            a.ChainedTargetSeats[a.ChainedTargetIndex - 1] == a.TargetSeat &&
            ProgramEventHistory<ChainedDamagePropagatedEvent>().Any(e => e.ResolutionId == use.Id && e.SourceSeat == use.SourceSeat &&
                e.TargetSeat == a.TargetSeat && e.Nature == DamageNature.Thunder);
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 42701)]
    private bool CompleteOlLuShiLeijieAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || LifecycleCardUse(attack.ResolutionId) is not { OlLuShiLeijie: not null } use || !IsOlLuShiLeijieNativeSlash(use)) return false;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, CardKind.ThunderSlash));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
    [NativeUseValidation(42701)] private void ValidateOlLuShiLeijieSlash(CardUseFrame use)
    { if (use.OlLuShiLeijie is not null && (!IsOlLuShiLeijieNativeSlash(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Leijie requires its exact whole native ThunderSlash before returning."); }
    [NativeUseReturn(42701)] private void ReturnOlLuShiLeijieSlash(CardUseFrame use)
    {
        if (use.OlLuShiLeijie is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId); var r = f.OlLuShi!;
        if (r.UseId != use.Id || r.ActionId != o.ActionId || r.NativeReturned || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Leijie cannot repeat or skip a native use return.");
        ReplaceRuntimeTop(f with { OlLuShi = r with { NativeReturned = true } });
        if (use.CardAttack is not { Active: true }) CompleteOlLuShiLeijieSlash(o.FrameId);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 42701)] private bool ReturnOlLuShiLeijieAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlLuShi: { Stage: OlLuShiStage.NativeUse, NativeReturned: true } r } f || r.UseId != completion.ResolutionId) return false;
        if (completion.ProgramFrameId != f.Id) throw new InvalidOperationException("Leijie's ThunderSlash returned to another parent."); CompleteOlLuShiLeijieSlash(f.Id); return true;
    }
    private void CompleteOlLuShiLeijieSlash(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.OlLuShi!;
        if (!r.NativeReturned || LifecycleCardUse(r.UseId) is not null || ProgramEventHistory<OlLuShiSlashReturnedEvent>().Any(e => e.FrameId == id && e.Ordinal == r.SlashCount)) throw new InvalidOperationException("Leijie cannot continue before its once-only native return.");
        AdvanceEventRulesAndQueueFact(new OlLuShiSlashReturnedEvent(id, r.SlashCount, r.UseId, r.ActionId));
        ReplaceRuntimeTop(f with { OlLuShi = r with { Stage = OlLuShiStage.Children } }); ContinueOlLuShi(id);
    }
}
