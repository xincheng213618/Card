namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasFanJiangZhangDaCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveJueshengLimitedDuel);
    private bool JueshengSpent(int seat) => CompleteProgramEventHistory().OfType<JueshengSpentEvent>().Any(e => e.OwnerSeat == seat && e.SkillId == "ol:juesheng");
    private bool CanIssueJueshengDuel(int actor, int target) => CanIssueSelectedActorDuel(target, actor);
    [ProgramCapability] private sealed class FanJiangZhangDaCapability(GameEngine e) : IFanJiangZhangDaHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginJuesheng(f); }
    [ProgramCapability] private sealed class JueshengSelectionQuery(GameEngine e) : IJueshengTargetQuery
    { public bool Allows(int actor, int target) => e.CanIssueJueshengDuel(actor, target); }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 27350, SkillProgramEffectOp.ResolveJueshengLimitedDuel)]
    private bool CanActivateJuesheng(CharacterState owner) => !JueshengSpent(owner.Seat) && _players.Any(p => CanIssueJueshengDuel(owner.Seat, p.Seat));
    private SkillProgramStepOutcome BeginJuesheng(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Juesheng is not null || f.SkillId != "ol:juesheng" || f.ActivationId != "limited-duel" || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var target] || JueshengSpent(f.OwnerSeat) || _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !CanIssueJueshengDuel(f.OwnerSeat, target) || ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("Juesheng requires its unspent owner, exact actual Play activation and legal Duel target.");
        var origin = new JueshengDuelOrigin(f.Id, _resolutionSequence + 1, new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId), f.GameplayHash, _turnNumber, f.OwnerSeat, target);
        ReplaceRuntimeTop(f = f with { Juesheng = new(origin, f.InstructionIndex, JueshengStage.NativeUse, [], []) });
        AdvanceEventRulesAndQueueFact(new JueshengSpentEvent(f.Id, f.OwnerSeat, f.SkillId));
        AdvanceEventRulesAndQueueFact(new JueshengDuelIssuedEvent(origin, f.InstructionIndex));
        var card = new Card(0, CardKind.Duel, Suit.None, 0);
        var id = BeginCardUse(card, f.OwnerSeat, [target], CardKind.Duel, physicalCardIds: [], jueshengDuel: origin);
        if (id != origin.UseId) throw new InvalidOperationException("Juesheng changed its reserved native Use identity.");
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, [target], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private int PrepareJueshengDuelDamage(long useId, int targetSeat)
    {
        if (LifecycleCardUse(useId) is not { JueshengDuel: { } origin } use) return 1;
        if (!IsJueshengDuelUse(useId) || !use.TargetSeats.Contains(targetSeat)) throw new InvalidOperationException("Juesheng damage belongs to one actual finalized native Duel opponent.");
        var f = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == origin.ProgramFrameId); var r = f.Juesheng!;
        if (r.Targets.SingleOrDefault(t => t.TargetSeat == targetSeat) is { } frozen) return frozen.Amount;
        var count = LifetimeSlashUseCount(targetSeat); var entry = new JueshengTargetDamage(targetSeat, count, Math.Max(1, count));
        ReplaceRuntimeFrame(f.Id, f with { Juesheng = r with { Targets = r.Targets.Append(entry).ToArray() } });
        AdvanceEventRulesAndQueueFact(new JueshengTargetDamageFrozenEvent(f.Id, useId, r.Targets.Count, targetSeat, entry.SlashCount, entry.Amount));
        return entry.Amount;
    }
    private bool JueshengActorMatches(CardUseFrame use, JueshengDuelOrigin origin)
    {
        if (use.Action is not { } action || action.ProviderSeat != origin.ActorSeat) return false;
        var actor = origin.ActorSeat;
        foreach (var e in ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Where(e => e.CardUseFrameId == use.Id))
        {
            if (e.PreviousActorSeat != actor || e.OwnerSeat != actor || e.ProviderSeat != origin.ActorSeat) return false;
            actor = e.ActorSeat;
        }
        return actor == use.SourceSeat && actor == action.ActorSeat;
    }
    private bool IsJueshengDuelUse(long id)
    {
        if (LifecycleCardUse(id) is not { JueshengDuel: { } origin, CardId: 0, CardKind: CardKind.Duel, PhysicalCardIds.Count: 0,
                Action: { Type: CardActionType.Use, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: null, EffectiveRank: null, EffectiveIsRed: null } } use ||
            origin.UseId != id || !JueshengActorMatches(use, origin) || ProgramEventHistory<JueshengDuelIssuedEvent>().Count(e => e.Origin == origin && e.InstructionIndex == 1) != 1) return false;
        var at = _resolutionStack.FindIndex(p => p.Id == id);
        return at > 0 && _resolutionStack[at - 1] is ProgramSkillFrame { Juesheng: { Stage: JueshengStage.NativeUse, NativeCompleted: false } r } f &&
            f.Id == origin.ProgramFrameId && r.Origin == origin && f.SkillId == origin.Source.SkillId && f.SkillInstanceId == origin.Source.SkillInstanceId && f.GameplayHash == origin.GameplayHash && r.InstructionIndex == f.InstructionIndex;
    }
    private long? JueshengDuelProgramParent(long id) => IsJueshengDuelUse(id) ? LifecycleCardUse(id)!.JueshengDuel!.ProgramFrameId : null;
    private bool IsJueshengDuelAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) => IsJueshengDuelUse(attack.ResolutionId) &&
        attack.Card is null && attack.PhysicalCards.Count == 0 && processing.Count == 0 && attack.EffectiveCardKind == CardKind.Duel &&
        attack.ProgramSkillCardUseFrameId == LifecycleCardUse(attack.ResolutionId)!.JueshengDuel!.ProgramFrameId;
    [NativeUseValidation(27350)] private void ValidateJueshengNative(CardUseFrame use)
    { if (use.JueshengDuel is not null && !IsJueshengDuelUse(use.Id)) throw new InvalidOperationException("Juesheng lost its zero-entity native Duel, final actor chain or owning paid operation."); }
    [NativeUseReturn(27350)] private void ReturnJueshengNative(CardUseFrame use)
    {
        if (use.JueshengDuel is not { } origin) return;
        var f = GetActiveProgramFrame(origin.ProgramFrameId); var r = f.Juesheng;
        if (r is not { Stage: JueshengStage.NativeUse, NativeCompleted: false } || r.Origin != origin || use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardKind == CardKind.Duel && e.CardId == 0) != 1)
            throw new InvalidOperationException("Juesheng must return once after the entire native Duel, every target and extra effect round.");
        var targets = use.TargetSeats.Distinct().ToArray();
        ReplaceRuntimeTop(f with { Juesheng = r with { NativeCompleted = true, FinalTargets = targets } });
        for (var i = 0; i < targets.Length; i++) AdvanceEventRulesAndQueueFact(new JueshengFinalTargetEvent(f.Id, use.Id, i, targets[i]));
        if (use.CardAttack is { Active: true }) return;
        SettleJueshengNative(origin);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 27350)]
    private bool ReturnJueshengAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Juesheng: { Stage: JueshengStage.NativeUse, NativeCompleted: true } r } || completion.ResolutionId != r.Origin.UseId) return false;
        if (completion.ProgramFrameId is { } parent && parent != r.Origin.ProgramFrameId) throw new InvalidOperationException("Juesheng's complete native Duel returned to another program.");
        SettleJueshengNative(r.Origin); return true;
    }
    private void SettleJueshengNative(JueshengDuelOrigin origin)
    {
        var f = GetActiveProgramFrame(origin.ProgramFrameId); var r = f.Juesheng!;
        if (!r.NativeCompleted || r.Origin != origin || LifecycleCardUse(origin.UseId) is not null || ProgramEventHistory<JueshengDuelReturnedEvent>().Any(e => e.UseId == origin.UseId))
            throw new InvalidOperationException("Juesheng cannot issue the then-clause before or twice after its whole native Use.");
        ReplaceRuntimeTop(f with { Juesheng = r with { Stage = JueshengStage.GrantChildren } });
        AdvanceEventRulesAndQueueFact(new JueshengDuelReturnedEvent(f.Id, origin.UseId, r.FinalTargets.Count));
        ContinueJuesheng(f.Id);
    }
    private void ContinueJuesheng(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Juesheng: { } r } f || f.Id != id) return;
        AssertJuesheng(f);
        if (r.Stage != JueshengStage.GrantChildren) return;
        while (true)
        {
            if (TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id)) return;
            f = GetActiveProgramFrame(id); r = f.Juesheng!;
            if (r.GrantCursor == r.FinalTargets.Count) break;
            var index = r.GrantCursor; var seat = r.FinalTargets[index];
            ReplaceRuntimeTop(f = f with { Juesheng = r with { GrantCursor = index + 1 } });
            if (!_players[seat].IsAlive) { AdvanceEventRulesAndQueueFact(new JueshengGrantSkippedEvent(f.Id, r.Origin.UseId, index, seat)); continue; }
            var source = $"juesheng:{f.Id}:{r.Origin.UseId}:{index}:{seat}"; var grantId = source + ":ol:juesheng";
            var instance = GetSkillBindingShard(_players[seat]).ActiveGrants.Where(g => g.SkillId == "ol:juesheng").OrderBy(g => g.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault()?.SkillInstanceId ?? $"juesheng-shared:{seat}";
            var cutoff = CompleteProgramEventHistory().Count();
            _players[seat].SkillGrants.Grant(new(grantId, "ol:juesheng", instance, source));
            AdvanceEventRulesAndQueueFact(new JueshengSkillGrantedEvent(f.Id, r.Origin.UseId, index, seat, grantId, instance, source, cutoff));
        }
        AdvanceEventRulesAndQueueFact(new JueshengCompletedEvent(f.Id, r.Origin.UseId));
        ReplaceRuntimeTop(f = f with { Juesheng = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27350)]
    private void ExpireJueshengSkills(ActualTurnCursorReturnedEvent returned)
    {
        if (!HasFanJiangZhangDaCapability) return;
        var history = CompleteProgramEventHistory().ToArray();
        foreach (var issued in history.OfType<JueshengSkillGrantedEvent>().Where(e => e.RecipientSeat == returned.OwnerSeat))
        {
            if (history.OfType<JueshengSkillExpiredEvent>().Any(e => e.GrantId == issued.GrantId)) continue;
            var start = history.Skip(issued.HistoryCutoff).OfType<TurnStartedEvent>().FirstOrDefault(e => e.ActorSeat == issued.RecipientSeat);
            if (start?.TurnNumber != returned.ActualTurn) continue;
            var grant = _players[issued.RecipientSeat].SkillGrants.Grants.SingleOrDefault(g => g.GrantId == issued.GrantId);
            if (grant is not null && (grant.SkillId != "ol:juesheng" || grant.SourceId != issued.SourceId || grant.SkillInstanceId != issued.SkillInstanceId)) throw new InvalidOperationException("Juesheng may expire only its original independently issued grant.");
            if (grant is not null) _players[issued.RecipientSeat].SkillGrants.RemoveGrant(grant.GrantId);
            AdvanceEventRulesAndQueueFact(new JueshengSkillExpiredEvent(issued.RecipientSeat, issued.GrantId, issued.SkillInstanceId, issued.SourceId, returned.ActualTurn));
        }
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27350)]
    private bool ResumeJuesheng(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Juesheng: not null } f || f.Id != id) return false; ContinueJuesheng(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27350)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27350)]
    private bool ContinuePaidJuesheng(ProgramSkillFrame f) { if (f.Juesheng is null) return false; AssertJuesheng(f); return true; }
}
