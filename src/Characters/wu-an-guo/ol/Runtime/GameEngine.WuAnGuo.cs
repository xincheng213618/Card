namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LiyongGainReason = "skill-program.wu-an-guo.liyong.obtain";
    private bool HasWuAnGuoCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel);
    private int WuAnGuoUsedSuitMask(int seat) => !HasWuAnGuoCapability ? 0 : CompleteProgramEventHistory().OfType<WuAnGuoActualSuitUsedEvent>()
        .Where(e => e.ActualTurn == _turnNumber && e.TurnOwnerSeat == _currentSeat && e.ActorSeat == seat).Aggregate(0, (bits, e) => bits | ActualTurnSuitBit(e.Suit));
    private bool IsWuAnGuoYangConversionAvailable(CharacterState owner, Card card, CardConversionSource source, bool forResponse)
    {
        if (source.SkillId != "ol:liyong" || source.BindingId != "yang-unused-suit") return true;
        var bit = ActualTurnSuitBit(EffectiveSuit(owner, card));
        return HasWuAnGuoCapability && !forResponse && source.OwnerSeat == owner.Seat && _phase == TurnPhase.Play && _currentSeat == owner.Seat && owner.IsAlive &&
            HasRuntimeSkillInstance(owner, source.SkillId, source.SkillInstanceId) && !GetProgramBooleanState(owner.Seat, source.SkillId, source.SkillInstanceId, "yin") &&
            bit != 0 && (WuAnGuoUsedSuitMask(owner.Seat) & bit) == 0;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 27010)]
    private void ObserveWuAnGuoActualUses(IGameEvent payload)
    {
        if (!HasWuAnGuoCapability || !HasOngoingActualUseTurn) return;
        CardActionContext? action = null; CardUseFrame? use = null;
        if (payload is CardUseDeclaredEvent declared)
        {
            use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == declared.ResolutionId);
            if (use is null || use.SourceSeat != declared.SourceSeat || use.CardKind != declared.CardKind || use.CardId != declared.CardId)
                throw new InvalidOperationException("Liyong suit history requires the exact native declared use.");
            action = use.Action;
            if (action is null || action.Type != CardActionType.Use || action.ActorSeat != use.SourceSeat || action.EffectiveKind != use.CardKind) return;
        }
        else if (payload is CardActionAcceptedEvent accepted && accepted.Action is { Type: CardActionType.Response } response)
        {
            if (response.ActorSeat != response.ProviderSeat || response.ResponderSeat != response.ActorSeat || response.RequesterSeat is not null) return;
            if (response.EffectiveKind == CardKind.Nullification)
            {
                var window = _resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault();
                if (window is null || !HasNullificationEffectOwner(window) || response.ParentActionId != NullificationParentActionId(window) || response.OpponentSeat != window.SourceSeat)
                    throw new InvalidOperationException("Liyong counterspell suit requires its exact native use node.");
            }
            else if (response.EffectiveKind == CardKind.Dodge)
            {
                var parent = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Action?.ActionId == response.ParentActionId);
                if (parent?.CardAttack is not { EffectiveCardKind: { } kind } attack || !IsSlashCard(kind) || attack.TargetSeat != response.ActorSeat || response.OpponentSeat != attack.SourceSeat) return;
            }
            else return;
            action = response;
        }
        if (action is null || !IsValidPlayerSeat(action.ActorSeat)) return;
        if (action.EffectiveSuit is { } suit && ActualTurnSuitBit(suit) != 0 && !CompleteProgramEventHistory().OfType<WuAnGuoActualSuitUsedEvent>().Any(e => e.ActionId == action.ActionId))
            AdvanceEventRulesAndQueueFact(new WuAnGuoActualSuitUsedEvent(_turnNumber, _currentSeat, action.ActorSeat, action.ActionId, suit));
        if (use is null) return;
        foreach (var source in action.ConversionChain.Where(s => s.SkillId == "ol:liyong" && s.BindingId == "yang-unused-suit"))
        {
            if (source.OwnerSeat != action.ActorSeat || action.PhysicalCards is not [var physical] ||
                physical.From.OwnerSeat != source.OwnerSeat || physical.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                action.EffectiveKind != CardKind.Duel || action.EffectiveSuit is not { } usedSuit || ActualTurnSuitBit(usedSuit) == 0 ||
                GetProgramBooleanState(source.OwnerSeat, source.SkillId, source.SkillInstanceId, "yin") ||
                CompleteProgramEventHistory().OfType<WuAnGuoActualSuitUsedEvent>().Any(e => e.ActualTurn == _turnNumber && e.TurnOwnerSeat == _currentSeat &&
                    e.ActorSeat == action.ActorSeat && e.Suit == usedSuit && e.ActionId != action.ActionId))
                throw new InvalidOperationException("Liyong Yang requires its original unused effective suit, one native owned physical payment and current source generation.");
            SetProgramBooleanState(source.OwnerSeat, source.SkillId, source.SkillInstanceId, "yin", true);
            AdvanceEventRulesAndQueueFact(new WuAnGuoLiyongYangConsumedEvent(use.Id, action.ActionId, source, _turnNumber, usedSuit));
        }
    }
    [ProgramCapability]
    private sealed class WuAnGuoCapability(GameEngine engine) : IWuAnGuoProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginWuAnGuoLiyong(frame); }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 27010, SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel)]
    private bool CanActivateWuAnGuoLiyong(CharacterState owner) => _players.Any(actor => CanIssueWuAnGuoLiyongDuel(actor.Seat, owner.Seat));
    private SkillProgramStepOutcome BeginWuAnGuoLiyong(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id); var mask = WuAnGuoUsedSuitMask(f.OwnerSeat);
        if (f.WuAnGuoLiyong is not null || f.SkillId != "ol:liyong" || f.ActivationId != "yin-obtain-and-incoming-duel" || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var actor] || _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !GetProgramBooleanState(f, "yin") ||
            !CanIssueWuAnGuoLiyongDuel(actor, f.OwnerSeat))
            throw new InvalidOperationException("Liyong Yin requires its current real Play state, earlier actual used suits and native legal incoming Duel actor.");
        var candidates = _cardZones.CardsAt(CardLocation.DrawPile).Where(c => (ActualTurnSuitBit(c.Suit) & mask) != 0).OrderBy(c => c.Id).ToArray();
        var card = candidates.Length == 0 ? null : candidates[_random.Next(candidates.Length)];
        var source = new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
        ReplaceRuntimeTop(f = f with { WuAnGuoLiyong = new(source, f.GameplayHash, f.InstructionIndex, _turnNumber, _cardUseDebitPhaseInstanceId, actor, mask,
            _movementSequence, _movementSequence, card?.Id) });
        AdvanceEventRulesAndQueueFact(new WuAnGuoLiyongYinStartedEvent(f.Id, source, f.GameplayHash, _turnNumber, _cardUseDebitPhaseInstanceId, actor, mask));
        SetProgramBooleanState(f, "yin", false);
        if (card is not null) MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(LiyongGainReason), movement =>
        {
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { WuAnGuoLiyong = current.WuAnGuoLiyong! with { After = movement.Sequence } });
        });
        f = GetActiveProgramFrame(f.Id); var r = f.WuAnGuoLiyong!;
        AdvanceEventRulesAndQueueFact(new WuAnGuoLiyongGainPaidEvent(f.Id, r.ObtainedCardId, r.Before, r.After));
        ContinueWuAnGuoLiyong(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void ContinueWuAnGuoLiyong(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuAnGuoLiyong: { } r } f || f.Id != id) return;
        AssertWuAnGuoLiyong(f);
        if (r.Stage != WuAnGuoLiyongStage.GainChildren) throw new InvalidOperationException("An issued Liyong Duel must return through its native typed completion.");
        if (TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !CanIssueWuAnGuoLiyongDuel(r.InitialActorSeat, f.OwnerSeat)) { FinishWuAnGuoLiyong(f, false); return; }
        var origin = new WuAnGuoLiyongDuelOrigin(f.Id, f.InstructionIndex, _resolutionSequence + 1, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstance, r.InitialActorSeat, f.OwnerSeat);
        ReplaceRuntimeTop(f = f with { WuAnGuoLiyong = r with { Stage = WuAnGuoLiyongStage.DuelIssued, Duel = origin } });
        AdvanceEventRulesAndQueueFact(new WuAnGuoLiyongDuelIssuedEvent(origin)); BeginWuAnGuoLiyongDuel(f, origin);
    }
    private void FinishWuAnGuoLiyong(ProgramSkillFrame f, bool issued)
    {
        if (!ValidWuAnGuoLiyong(f) || issued != (f.WuAnGuoLiyong!.Stage == WuAnGuoLiyongStage.DuelIssued)) throw new InvalidOperationException("Liyong lost its paid gain or exact issued native Duel return.");
        AdvanceEventRulesAndQueueFact(new WuAnGuoLiyongCompletedEvent(f.Id, issued)); ReplaceRuntimeTop(f = f with { WuAnGuoLiyong = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27010)]
    private bool ResumeWuAnGuoLiyong(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuAnGuoLiyong: not null } f || f.Id != id) return false; ContinueWuAnGuoLiyong(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27010)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27010)]
    private bool ContinuePaidWuAnGuoLiyong(ProgramSkillFrame f) => f.WuAnGuoLiyong is not null && ValidWuAnGuoLiyong(f);
}
