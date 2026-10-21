namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int[] ShenJwSlashTargets(ProgramSkillFrame f) => CurrentTableOrder().Select(seat => _players[seat]).Where(p => CanUseVirtualSlashTarget(_players[f.OwnerSeat], p, CardKind.FireSlash,
        physicalSuit: Suit.None, effectiveRank: 0, countsTowardLimit: false)).Select(p => p.Seat).ToArray();

    // Same native pipeline as an offered zero-material Slash, retaining the exact
    // FireSlash appearance and returning each whole use before offering the next.
    private void IssueShenJwSlash(ProgramSkillFrame f, int targetSeat)
    {
        var r = f.ShenJiangWei!;
        if (r.Stage != ShenJiangWeiStage.SlashChoice || r.SlashCount >= 9 || !ShenJwSlashTargets(f).Contains(targetSeat) || ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("Pingxiang cannot issue an overlapping or illegal native fire Slash.");
        ReplaceRuntimeTop(f = f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.NativeIssuing, SlashCount = r.SlashCount + 1, TargetSeat = targetSeat, NativeReturned = false } });
        var source = _players[f.OwnerSeat]; var target = _players[targetSeat]; var id = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, f.OwnerSeat, f.OwnerSeat, null, null, null, CardKind.FireSlash, [targetSeat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        var origin = new ShenJiangWeiSlashOrigin(f.Id, r.Source, r.GameplayHash, r.SlashCount + 1, id, action.ActionId, targetSeat);
        ReplaceRuntimeTop(f with { ShenJiangWei = f.ShenJiangWei! with { Stage = ShenJiangWeiStage.NativeUse, UseId = id, ActionId = action.ActionId } });
        PushRuntimeFrame(new CardUseFrame(id, f.OwnerSeat, 0, CardKind.FireSlash, [targetSeat], PhysicalCardIds: []) { Action = action, UnlimitedUse = true, ShenJiangWeiPingxiang = origin });
        AdvanceEventRulesAndQueueFact(new ShenJiangWeiSlashIssuedEvent(origin));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, CardKind.FireSlash, f.OwnerSeat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, [targetSeat]));
        var attack = new CardAttackHandle(this, id, f.OwnerSeat, targetSeat, card: null,
            damageAmount: 1 + source.AlcoholDamageBonus, playedCardKind: CardKind.FireSlash,
            ignoresArmor: HasCardArmorBypass(source, target, CardKind.FireSlash), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(id, source); source.HasAlcoholEffect = false; ActiveCardAttack = attack;
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(source.Seat, CardKind.FireSlash);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.FireSlash, f.OwnerSeat, targetSeat)); TryMarkProgramUseCommitted(id);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private bool IsShenJwNativeSlash(CardUseFrame use)
    {
        if (use.ShenJiangWeiPingxiang is not { } o || use.CardId != 0 || use.CardKind != CardKind.FireSlash || use.UnlimitedUse != true ||
            use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.FireSlash, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a ||
            use.Id != o.UseId || a.ActionId != o.ActionId || a.ActorSeat != o.Source.OwnerSeat || a.ProviderSeat != o.Source.OwnerSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is not ProgramSkillFrame { ShenJiangWei: { Operation: SkillProgramEffectOp.ResolveShenJiangWeiPingxiang, Stage: ShenJiangWeiStage.NativeUse } r } f ||
            r.Source != o.Source || r.GameplayHash != o.GameplayHash || r.SlashCount != o.Ordinal || r.TargetSeat != o.TargetSeat || r.UseId != use.Id || r.ActionId != a.ActionId ||
            ProgramEventHistory<ShenJiangWeiSlashIssuedEvent>().Count(e => e.Origin == o) != 1 || !HasShenJwPaidPingxiang(f)) return false;
        return ShownEntityUseActorMatches(use, o.Source.OwnerSeat, o.Source.OwnerSeat);
    }
    private bool IsShenJwAttackConsistent(CardAttackHandle a, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(a.ResolutionId) is not { ShenJiangWeiPingxiang: { } o } use || !IsShenJwNativeSlash(use) ||
            a.Card is not null || a.PhysicalCards.Count != 0 || processing.Count != 0 || a.EffectiveCardKind != CardKind.FireSlash ||
            a.ProgramSkillCardUseFrameId != o.FrameId || a.ProgramSkillFrameId is not null || a.SourceSeat != use.SourceSeat || a.CardUserSeat != use.SourceSeat || a.IsSourceLess)
            return false;
        if (!a.IsChainPropagation) return use.TargetSeats.Contains(a.TargetSeat) || a.DamageRedirected;
        // Chain propagation is the same native attack's frozen recipient cursor;
        // do not create a program-damage parent or a second virtual-card origin.
        return a.ChainedTargetIndex > 0 && a.ChainedTargetIndex <= a.ChainedTargetSeats.Count &&
            a.ChainedTargetSeats[a.ChainedTargetIndex - 1] == a.TargetSeat &&
            ProgramEventHistory<ChainedDamagePropagatedEvent>().Any(e => e.ResolutionId == use.Id && e.SourceSeat == use.SourceSeat &&
                e.TargetSeat == a.TargetSeat && e.Nature == DamageNature.Fire);
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 38400)]
    private bool CompleteShenJwAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || LifecycleCardUse(attack.ResolutionId) is not { ShenJiangWeiPingxiang: not null } use || !IsShenJwNativeSlash(use)) return false;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, CardKind.FireSlash));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
    [NativeUseValidation(38400)] private void ValidateShenJwSlash(CardUseFrame use)
    { if (use.ShenJiangWeiPingxiang is not null && (!IsShenJwNativeSlash(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Pingxiang requires its exact whole native FireSlash before returning."); }
    [NativeUseReturn(38400)] private void ReturnShenJwSlash(CardUseFrame use)
    {
        if (use.ShenJiangWeiPingxiang is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId); var r = f.ShenJiangWei!;
        if (r.UseId != use.Id || r.ActionId != o.ActionId || r.NativeReturned || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Pingxiang cannot repeat or skip a native use return.");
        ReplaceRuntimeTop(f with { ShenJiangWei = r with { NativeReturned = true } });
        if (use.CardAttack is not { Active: true }) CompleteShenJwSlash(o.FrameId);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 38400)] private bool ReturnShenJwAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenJiangWei: { Stage: ShenJiangWeiStage.NativeUse, NativeReturned: true } r } f || r.UseId != completion.ResolutionId) return false;
        if (completion.ProgramFrameId != f.Id) throw new InvalidOperationException("Pingxiang's FireSlash returned to another parent."); CompleteShenJwSlash(f.Id); return true;
    }
    private void CompleteShenJwSlash(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.ShenJiangWei!;
        if (!r.NativeReturned || LifecycleCardUse(r.UseId) is not null || ProgramEventHistory<ShenJiangWeiSlashReturnedEvent>().Any(e => e.FrameId == id && e.Ordinal == r.SlashCount)) throw new InvalidOperationException("Pingxiang cannot continue before its once-only native return.");
        AdvanceEventRulesAndQueueFact(new ShenJiangWeiSlashReturnedEvent(id, r.SlashCount, r.UseId, r.ActionId));
        ReplaceRuntimeTop(f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.SlashChildren } }); ContinueShenJw(id);
    }
}
