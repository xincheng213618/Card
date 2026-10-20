using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record HellSoulProgramReturn(long CardUseFrameId, long ActionId, int TargetIndex, int TargetSeat);
public sealed record HellSoulResolution(long ProgramFrameId, long ActionId, int TargetIndex, int TargetSeat);
public sealed record HellSoulRestoreReceipt(int InstructionIndex, int TargetSeat, int RestoredGrants);
public sealed record HellSoulRestoreIssuedEvent(long ProgramFrameId, long CardUseFrameId, int TargetSeat, int RestoredGrants) : IGameEvent;
public sealed record HellSoulGrantRestoredEvent(long ProgramFrameId, long CardUseFrameId, long LossFrameId,
    int TargetSeat, string GrantId, string Outcome) : IGameEvent;
public sealed record HellSoulTargetResolvedEvent(long CardUseFrameId, int TargetSeat, int RestoredGrants, bool Completed) : IGameEvent;
public sealed record HellReturnSoulDestroyedEvent(int CardId, int DiscardSequence, int DestroySequence) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HellSoulProgramReturn? HellSoulReturn { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HellSoulRestoreReceipt? HellSoulRestore { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HellSoulResolution? HellSoul { get; init; }
}

public sealed partial class GameEngine
{
    private const string HellSoulResolutionSkill = "hell:return-soul-resolution";
    private static string HellSoulGrant(long id) => $"card-use:hell-return-soul:{id}";
    private string HellSoulGrantSource(long id) => $"turn:{_turnNumber}:{_currentSeat}:card-use:hell-return-soul:{id}";

    private HellWanghunGrantLostEvent[] HellSoulOutstandingLosses(int seat)
    {
        var restored = ProgramEventHistory<HellSoulGrantRestoredEvent>()
            .Select(e => (e.LossFrameId, e.TargetSeat, e.GrantId)).ToHashSet();
        return ProgramEventHistory<HellWanghunGrantLostEvent>().Where(e => e.TargetSeat == seat &&
            !restored.Contains((e.FrameId, e.TargetSeat, e.Grant.GrantId)))
            .OrderBy(e => e.FrameId).ThenBy(e => e.Grant.GrantId, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<int> HellReturnSoulTargets(int actorSeat) => Array.AsReadOnly(
        Enumerable.Range(0, _playerCount).Select(offset => (actorSeat + offset) % _playerCount)
            .Where(seat => _players[seat].IsAlive && HellSoulOutstandingLosses(seat).Length != 0).ToArray());

    private void AddHellSoulActions(List<LegalAction> actions, CharacterState actor, IEnumerable<Card> cards)
    {
        if (IsCardUseForbidden(actor.Seat, CardKind.ReturnSoul, CardActionType.Use)) return;
        foreach (var card in cards.Where(card => card.Kind == CardKind.ReturnSoul))
        {
            var targets = HellReturnSoulUseTargets(actor, card);
            if (targets.Count != 0)
                actions.Add(new(LegalActionKind.ReturnSoul, card.Id, null, "使用【回魂】，令因【忘魂】失去技能且尚未恢复的存活角色恢复这些技能",
                    TargetSeats: targets));
        }
    }
    private IReadOnlyList<int> HellReturnSoulUseTargets(CharacterState actor, Card card) =>
        ApplyTurnCardGroupTargetRestrictions(actor, HellReturnSoulTargets(actor.Seat).Where(seat =>
            !IsDirectedCardTargetProhibited(actor.Seat, seat, CardKind.ReturnSoul) &&
            !IsCardTargetProhibited(_players[seat], CardKind.ReturnSoul, card.Suit, SuitColor(EffectiveSuit(actor, card))) &&
            !IsShenwuRobeTargetProhibited(actor.Seat, seat, CardKind.ReturnSoul) &&
            !HasCardPolicy(_players[seat], SkillProgramCardPolicyKind.ExcludeGlobalTarget, CardKind.ReturnSoul) &&
            !HasBeneficiarySuitShield(actor.Seat, seat, EffectiveSuit(actor, card))).ToArray());
    private void ResolveHellReturnSoul(CharacterState actor, Card card)
    {
        if (!BuildLegalActions(actor, selectedPhysicalCardId: card.Id).Any(a => a.Kind == LegalActionKind.ReturnSoul && a.CardId == card.Id))
            throw new InvalidOperationException("Return soul lost its actual playable entity or outstanding Wanghun target.");
        var targets = HellReturnSoulUseTargets(actor, card);
        var id = BeginCardUse(card, actor.Seat, targets);
        MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, targets, LegalActionKind.ReturnSoul);
    }

    private void ResolveHellReturnSoulEffect(NullificationWindowFrame pending)
    {
        var use = LifecycleCardUse(pending.ParentFrameId) ?? throw new InvalidOperationException("Return soul lost its actual native card owner.");
        var seat = GetNullificationTargetSeat(pending) ?? throw new InvalidOperationException("Return soul lost its exact native target.");
        if (use.CardKind != CardKind.ReturnSoul || use.HellSoul is not null || use.Action is not { } action ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != seat)
            throw new InvalidOperationException("Return soul changed its issued native effect or target cursor.");
        SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        if (!_players[seat].IsAlive || HellSoulOutstandingLosses(seat).Length == 0)
        { FinishCardUse(use.Id, GetNullificationEffectCard(pending), CardKind.ReturnSoul); return; }
        var program = _contentRegistry.GetSkill(HellSoulResolutionSkill).Program!;
        var grant = HellSoulGrant(use.Id);
        _players[use.SourceSeat].SkillGrants.Grant(new(grant, HellSoulResolutionSkill, grant, HellSoulGrantSource(use.Id),
            TurnExpiry: new(_turnNumber, _currentSeat)));
        var childId = ++_resolutionSequence;
        UpdateLifecycleCardUse(use.Id, current => current with { HellSoul = new(childId, action.ActionId, use.TargetIndex, seat) });
        var child = new ProgramSkillFrame(childId, use.SourceSeat, HellSoulResolutionSkill, "card-effect",
            program.GameplayHash, 0, [], Array.AsReadOnly(new[] { seat }))
        { SkillInstanceId = grant, HellSoulReturn = new(use.Id, action.ActionId, use.TargetIndex, seat) };
        PushRuntimeFrame(child);
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(child.Id, child.OwnerSeat, child.SkillId, child.ActivationId));
        AdvanceRuntimeProgram(child.Id);
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 40053, SkillProgramEffectOp.RestoreHellWanghunSkills)]
    private bool AllowsHellSoulVoluntaryActivation(ProgramActivationLegalityContext context) => context.SkillId != HellSoulResolutionSkill;

    private bool IsExactHellSoulProgram(ProgramSkillFrame frame)
    {
        if (frame.HellSoulReturn is not { } returned || frame.SkillId != HellSoulResolutionSkill || frame.ActivationId != "card-effect" ||
            frame.TriggerId is not null || frame.SelectedCardIds.Count != 0 || !frame.SelectedTargetSeats.SequenceEqual([returned.TargetSeat]) ||
            frame.SkillInstanceId != HellSoulGrant(returned.CardUseFrameId) || frame.GameplayHash != _contentRegistry.GetSkill(frame.SkillId).Program!.GameplayHash ||
            LifecycleCardUse(returned.CardUseFrameId) is not { CardKind: CardKind.ReturnSoul, Step: ResolutionFrameStep.ResolvingEffect,
                HellSoul: { } current, Action: { } action } use ||
            current.ProgramFrameId != frame.Id || current.ActionId != action.ActionId || returned.ActionId != action.ActionId ||
            current.TargetIndex != use.TargetIndex || returned.TargetIndex != use.TargetIndex || current.TargetSeat != returned.TargetSeat ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != returned.TargetSeat ||
            use.SourceSeat != frame.OwnerSeat) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        return index > 0 && _resolutionStack[index - 1].Id == use.Id &&
            _players[frame.OwnerSeat].SkillGrants.Grants.Any(g => g.GrantId == frame.SkillInstanceId &&
                g.SkillId == frame.SkillId && g.SkillInstanceId == frame.SkillInstanceId && g.SourceId == HellSoulGrantSource(use.Id) &&
                g.TurnExpiry == new SkillGrantTurnExpiry(_turnNumber, _currentSeat));
    }

    private SkillProgramStepOutcome BeginHellSoulRestoration(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!IsExactHellSoulProgram(frame) || frame.InstructionIndex != 1 || frame.HellSoulRestore is not null ||
            ProgramEventHistory<HellSoulRestoreIssuedEvent>().Any(e => e.ProgramFrameId == frame.Id))
            throw new InvalidOperationException("Return soul restoration requires its exact once-issued native effect child.");
        var returned = frame.HellSoulReturn!;
        var target = _players[returned.TargetSeat];
        var losses = HellSoulOutstandingLosses(target.Seat);
        var decisions = losses.Select(loss =>
        {
            var existing = target.SkillGrants.Grants.SingleOrDefault(g => g.GrantId == loss.Grant.GrantId);
            var outcome = WasGrantPermanentlyRevokedAfterLoss(target.Seat, loss.Grant, loss) ? "permanently-lost" :
                !target.IsAlive ? "owner-dead" : target.General.Id != loss.GeneralId ||
                target.SecondaryGeneral?.Id != loss.SecondaryGeneralId ? "template-changed" :
                loss.Grant.TurnExpiry is { } turn && ProgramEventHistory<TurnEndedEvent>().Any(e =>
                    e.TurnNumber == turn.TurnNumber && e.ActorSeat == turn.TurnOwnerSeat) ? "grant-expired" :
                loss.Grant.PhaseExpiry is not null || HasNextActualTurnStartSkillGrantExpired(loss.Grant)
                    ? "grant-expired" : existing is null ? "restored" :
                existing == loss.Grant ? "already-present" : "source-changed";
            return (Loss: loss, Outcome: outcome);
        }).ToArray();
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var count = decisions.Count(d => d.Outcome == "restored");
        ReplaceRuntimeTop(frame = frame with { HellSoulRestore = new(frame.InstructionIndex, target.Seat, count) });
        AdvanceEventRulesAndQueueFact(new HellSoulRestoreIssuedEvent(frame.Id, returned.CardUseFrameId, target.Seat, count));
        foreach (var decision in decisions)
        {
            if (decision.Outcome == "restored") target.SkillGrants.Grant(decision.Loss.Grant);
            AdvanceEventRulesAndQueueFact(new HellSoulGrantRestoredEvent(frame.Id, returned.CardUseFrameId,
                decision.Loss.FrameId, target.Seat, decision.Loss.Grant.GrantId, decision.Outcome));
        }
        ContinueHellSoulRestoration(GetActiveProgramFrame(frame.Id));
        return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40053)]
    private bool ResumeHellSoulRestoration(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HellSoulRestore: not null } frame || frame.Id != id) return false;
        ContinueHellSoulRestoration(frame); return true;
    }
    private void ContinueHellSoulRestoration(ProgramSkillFrame frame)
    {
        if (!IsExactHellSoulProgram(frame) || frame.HellSoulRestore is not { } receipt ||
            receipt.InstructionIndex != frame.InstructionIndex || receipt.TargetSeat != frame.HellSoulReturn!.TargetSeat ||
            ProgramEventHistory<HellSoulRestoreIssuedEvent>().Count(e => e.ProgramFrameId == frame.Id &&
                e.CardUseFrameId == frame.HellSoulReturn.CardUseFrameId && e.TargetSeat == receipt.TargetSeat &&
                e.RestoredGrants == receipt.RestoredGrants) != 1)
            throw new InvalidOperationException("Return soul lost its exact already restored skill receipt.");
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id)) return;
        FinishProgramSkill(GetActiveProgramFrame(frame.Id), true);
    }
    private bool TryCompleteHellSoulProgram(ProgramSkillFrame frame, bool completed)
    {
        if (frame.HellSoulReturn is not { } returned) return false;
        if (!IsExactHellSoulProgram(frame) || _resolutionStack.LastOrDefault()?.Id != frame.Id)
            throw new InvalidOperationException("Return soul cannot return to a different native card or target.");
        var restored = frame.HellSoulRestore?.RestoredGrants ?? 0;
        AdvanceEventRulesAndQueueFact(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, completed));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        _players[frame.OwnerSeat].SkillGrants.RemoveGrant(frame.SkillInstanceId);
        var use = LifecycleCardUse(returned.CardUseFrameId)!;
        UpdateLifecycleCardUse(use.Id, current => current with { HellSoul = null });
        AdvanceEventRulesAndQueueFact(new HellSoulTargetResolvedEvent(use.Id, returned.TargetSeat, restored, completed));
        FinishCardUse(use.Id, GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true), CardKind.ReturnSoul);
        return true;
    }
    private bool HellSoulFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
        child is ProgramSkillFrame { HellSoulReturn: { } returned } frame && returned.CardUseFrameId == parent.Id && IsExactHellSoulProgram(frame);
    private bool HasHellSoulPrompt() => _resolutionStack.OfType<ProgramSkillFrame>()
        .Any(frame => frame.HellSoulReturn is not null && IsExactHellSoulProgram(frame));

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40053)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40053)]
    private bool CanContinueHellSoulProgram(ProgramSkillFrame frame) => frame.HellSoulReturn is not null && IsExactHellSoulProgram(frame);

    private void DestroyHellReturnSoulIfDiscarded(Card card, CardLocation to)
    {
        if (card.Kind != CardKind.ReturnSoul || to != CardLocation.DiscardPile || _cardZones.GetLocation(card.Id) != to) return;
        var entered = _cardMovements.Last(m => m.CardId == card.Id);
        if (entered.To != to) throw new InvalidOperationException("Return soul destruction lost its actual discard entry.");
        MoveCard(card, to, CardLocation.OutsideGame, new("activity.hell.return-soul-destroyed"));
        AdvanceEventRulesAndQueueFact(new HellReturnSoulDestroyedEvent(card.Id, entered.Sequence,
            _cardMovements.Last(m => m.CardId == card.Id).Sequence));
    }
    private bool IsHellDestroyedSoulMaterial(long useId, int cardId) => LifecycleCardUse(useId)?.Action is { } action &&
        action.PhysicalCards.Any(material => material.CardId == cardId && material.CardKind == CardKind.ReturnSoul) &&
        _cardZones.GetLocation(cardId) == CardLocation.OutsideGame &&
        ProgramEventHistory<HellReturnSoulDestroyedEvent>().Any(e => e.CardId == cardId &&
            _cardMovements.Any(m => m.CardId == cardId && m.Sequence == e.DiscardSequence && m.To == CardLocation.DiscardPile) &&
            _cardMovements.Any(m => m.CardId == cardId && m.Sequence == e.DestroySequence && m.From == CardLocation.DiscardPile &&
                m.To == CardLocation.OutsideGame && m.Reason.Value == "activity.hell.return-soul-destroyed"));

    private void AssertHellSoulCardState()
    {
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.HellSoulReturn is not null))
            if (!IsExactHellSoulProgram(frame)) throw new InvalidOperationException("Return soul changed its issued hidden restoration child.");
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(f => f.HellSoul is not null))
            if (use.CardKind != CardKind.ReturnSoul || !_resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
                    f.Id == use.HellSoul!.ProgramFrameId && IsExactHellSoulProgram(f)))
                throw new InvalidOperationException("Return soul lost its exact native target restoration child.");
    }
}
