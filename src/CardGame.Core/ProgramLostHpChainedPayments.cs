namespace CardGame.Core;

// Scalar receipt: the sole material is public only after its real discard.
public sealed record ProgramLostHpChainedPayment(int InstructionIndex, string SourceBind,
    int CardId, CardLocation From, CardLocation To, Suit EffectiveSuit, long SequenceBefore,
    long SequenceAfter, int ActualTurnNumber, int ActualTurnOwnerSeat, CardUseEffectSource Source);
public sealed record ProgramLostHpChainedCostPaidEvent(long ProgramFrameId, ProgramLostHpChainedPayment Payment) : IGameEvent;

public sealed partial class GameEngine
{
    private bool IsLostHpChainedComposition(ProgramSkillFrame frame)
    {
        if (frame.TriggerId is not null || frame.WindowContext is not null ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash ||
            ProgramInstructionResolver.Default.FindActivation(program, frame.ActivationId) is not { } activation ||
            activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 0 || activation.MaxTargets != 0 ||
            activation.UsesPerPhase != 1 || activation.UsesPerTurn is not null || activation.ContinueAfterOwnerDeath ||
            activation.Condition.Kind != SkillProgramConditionKind.Always || activation.Effects.Count != 7 ||
            activation.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always)) return false;
        var e = activation.Effects;
        return e[0] is { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner,
                MinimumCards: 1, MaximumCards: 1, ResultBind: not null } select &&
            select.Zones.Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) &&
            select.Suits.Order().SequenceEqual(new[] { Suit.Heart, Suit.Diamond }.Order()) &&
            e[1] is { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner,
                Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true, ExceptBind: null } move &&
            move.SourceBind == select.ResultBind &&
            e[2] is { Op: SkillProgramEffectOp.AwaitBoundCardMovements, Target: SkillProgramEffectTarget.Owner } &&
            e[3] is { Op: SkillProgramEffectOp.SelectTargets, Target: SkillProgramEffectTarget.Owner,
                NumberExpression: SkillProgramNumberExpression.OwnerLostHpAtLeastOne,
                TargetKind: SkillProgramTargetKind.AnyLiving, MinimumTargets: 1 } &&
            e[4] is { Op: SkillProgramEffectOp.SetChainedState, Target: SkillProgramEffectTarget.SelectedTargets, Chained: true } &&
            e[5] is { Op: SkillProgramEffectOp.SelectOneSelectedTarget, Target: SkillProgramEffectTarget.Owner } &&
            e[6] is { Op: SkillProgramEffectOp.Damage, Target: SkillProgramEffectTarget.SelectedTarget,
                Amount: 1, DamageNature: CardGame.Core.DamageNature.Fire, ActorReference: null, TargetReference: null };
    }
    private ProgramSkillFrame FreezeLostHpChainedPayment(ProgramSkillFrame frame, string sourceBind,
        string? exceptBind, SkillProgramCardDestination destination)
    {
        if (!HasLostHpTargetSelection(frame)) return frame;
        if (!IsLostHpChainedComposition(frame) || frame.InstructionIndex != 2 || frame.LostHpChainedPayment is not null ||
            destination != SkillProgramCardDestination.DiscardPile || exceptBind is not null ||
            !_players[frame.OwnerSeat].IsAlive || _winner != Winner.None ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            throw new InvalidOperationException("A lost-HP chain payment requires its still-qualified original seven-node activation.");
        var binding = frame.CardSetBindings.SingleOrDefault(b => b.Name == sourceBind);
        if (binding is null || binding.CardIds is not [var cardId] || binding.SourceLocations is not [var from] ||
            from.OwnerSeat != frame.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            binding.SelectionActorSeat is { } chooser && chooser != frame.OwnerSeat || _cardZones.GetLocation(cardId) != from)
            throw new InvalidOperationException("A lost-HP chain payment lost its exact original owned material.");
        var card = _cardZones.CardsAt(from).Single(c => c.Id == cardId);
        var suit = GetProgramEffectiveSuit(_players[frame.OwnerSeat], card);
        if (suit is not (Suit.Heart or Suit.Diamond) ||
            IsSelfHandCategoryDiscardForbidden(frame.OwnerSeat, card, from, OwnedCardMoveIntent.Discard) ||
            IsForeignEquipmentDiscardPrevented(frame.OwnerSeat, card, from, OwnedCardMoveIntent.Discard))
            throw new InvalidOperationException("The actual red material must remain a legal discard at payment.");
        var sequence = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        var to = card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile;
        ReplaceRuntimeTop(frame = frame with { LostHpChainedPayment = new(2, sourceBind, cardId, from, to, suit,
            sequence, sequence, _turnNumber, _currentSeat, CreateProgramTurnEffectSource(frame)) });
        return frame;
    }
    private void CommitLostHpChainedPayment(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.LostHpChainedPayment is not { } paid) return;
        var after = _cardMovements.LastOrDefault()?.Sequence ?? paid.SequenceBefore;
        var reason = $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.MoveBoundCards}";
        if (after <= paid.SequenceBefore || _cardMovements.Count(m => m.Sequence > paid.SequenceBefore && m.Sequence <= after &&
                m.CardId == paid.CardId && m.From == paid.From && m.To == paid.To && m.Reason.Value == reason) != 1)
            throw new InvalidOperationException("A lost-HP chain commitment requires its one actual red discard ledger entry.");
        ReplaceRuntimeTop(frame = frame with { LostHpChainedPayment = paid with { SequenceAfter = after } });
        AdvanceEventRulesAndQueueFact(new ProgramLostHpChainedCostPaidEvent(frame.Id, frame.LostHpChainedPayment!));
    }
    private bool ValidLostHpChainedPayment(ProgramSkillFrame frame)
    {
        if (frame.LostHpChainedPayment is not { } paid || !IsLostHpChainedComposition(frame) ||
            paid.InstructionIndex != 2 || frame.InstructionIndex is < 2 or > 7 ||
            paid.SequenceBefore < 0 || paid.SequenceAfter <= paid.SequenceBefore || paid.EffectiveSuit is not (Suit.Heart or Suit.Diamond) ||
            paid.Source != CreateProgramTurnEffectSource(frame) || paid.ActualTurnNumber != _turnNumber || paid.ActualTurnOwnerSeat != _currentSeat ||
            paid.From.OwnerSeat != frame.OwnerSeat || paid.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            paid.To != CardLocation.DiscardPile && paid.To != CardLocation.OutsideGame) return false;
        var effects = ProgramInstructionResolver.Default.FindActivation(_contentRegistry.GetSkill(frame.SkillId).Program!, frame.ActivationId)!.Effects;
        if (effects[1].SourceBind != paid.SourceBind || frame.CardSetBindings.SingleOrDefault(b => b.Name == paid.SourceBind) is not { } binding ||
            !binding.CardIds.SequenceEqual([paid.CardId]) || !binding.SourceLocations.SequenceEqual([paid.From])) return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<ProgramSkillStartedEvent>().Count(e => e.FrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat &&
                e.SkillId == frame.SkillId && e.ActivationId == frame.ActivationId) == 1 &&
            history.OfType<ProgramLostHpChainedCostPaidEvent>().Where(e => e.ProgramFrameId == frame.Id) is var issued &&
            issued.Count() == 1 && issued.Single().Payment == paid &&
            _cardMovements.Count(m => m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter && m.CardId == paid.CardId &&
                m.From == paid.From && m.To == paid.To && m.Reason.Value == $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.MoveBoundCards}") == 1;
    }
    private void AssertLostHpChainedPayment(ProgramSkillFrame frame)
    {
        if (frame.LostHpChainedPayment is not null && !ValidLostHpChainedPayment(frame))
            throw new InvalidOperationException("A paid lost-HP chain activation lost its original material, source or actual ledger.");
    }
    private bool CanContinuePaidLostHpChain(ProgramSkillFrame frame) => _winner == Winner.None &&
        _players[frame.OwnerSeat].IsAlive && ValidLostHpChainedPayment(frame);
    private bool ReturnLostHpChainedMovement(ProgramSkillFrame frame)
    {
        if (frame.LostHpChainedPayment is null) return false;
        AssertLostHpChainedPayment(frame);
        if (frame.InstructionIndex is not (2 or 3) || frame.PendingMovementContinuation is not { CoverageResultBind: null } pending ||
            pending.SubjectSeat != frame.OwnerSeat)
            throw new InvalidOperationException("The paid red discard lost its precise awaited-movement return.");
        if (TryDrainFireTargetMovement(frame)) return true;
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive)
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "红牌费用已结清，死亡或胜负取消未发行连环与火伤。");
        else AdvanceRuntimeProgram(frame.Id);
        return true;
    }
    private sealed partial class ProgramSkillHost
    { public bool CanContinuePaidLostHpChain(ProgramSkillFrame frame) => engine.CanContinuePaidLostHpChain(frame); }
}
