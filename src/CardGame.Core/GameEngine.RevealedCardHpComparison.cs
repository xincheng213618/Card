namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome LoseHpIfRevealedNonEquipmentDiffers(ProgramSkillFrame frame, string bind)
    {
        frame = GetActiveProgramFrame(frame.Id);
        var cards = GetProgramCardSet(frame, bind);
        if (frame.SelectedTargetSeats is not [var target] || !IsValidPlayerSeat(target) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not TurnEndingBoundaryFrame ending ||
            ending.Id != context.ParentFrameId || ending.ItemIndex < 0 || ending.ItemIndex >= ending.Items.Count ||
            ending.Items[ending.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(frame, candidate) ||
            !MatchesFrozenOwnOrPreviousEnding(candidate, context))
            throw new InvalidOperationException("A revealed-card HP comparison lost its exact own/previous Ending parent.");
        if (cards.CardIds.Count == 0) return SkillProgramStepOutcome.Continue;
        if (cards.CardIds is not [var id] || cards.Visibility != SkillProgramCardSetVisibility.Public ||
            NeighborDiscardHistory().OfType<ProgramCardsRevealedEvent>().LastOrDefault(e =>
                e.FrameId == frame.Id && e.SkillId == frame.SkillId && e.BindingId == frame.TriggerId &&
                e.OwnerSeat == frame.OwnerSeat && e.Bind == bind) is not { Cards: [var revealed] } || revealed.Id != id)
            throw new InvalidOperationException("The comparison requires its exact frozen publicly revealed draw entity.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[target].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(frame, "展示后的角色或技能来源已失效，未付后继体力成本。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var loses = !EquipmentCatalog.IsEquipment(revealed.Kind) && _players[target].Hp != _players[frame.OwnerSeat].Hp;
        AdvanceEventRulesAndQueueFact(new RevealedCardHpComparedEvent(frame.Id, frame.InstructionIndex - 1,
            new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), frame.GameplayHash,
            id, revealed.Kind, target, _players[frame.OwnerSeat].Hp, _players[target].Hp, loses));
        return loses ? new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, target, 1) : SkillProgramStepOutcome.Continue;
    }

    private bool IsRevealedHpComparisonProgramDying()
    {
        if (ActiveDying is not { Continuation: DyingContinuationKind.ProgramSkill, KillerSeat: null } dying)
            return false;
        var index = _resolutionStack.FindIndex(f => f.Id == dying.Id);
        if (index < 2 || _resolutionStack[index - 1] is not ProgramSkillFrame root ||
            dying.ParentFrameId != root.Id || root.SelectedTargetSeats is not [var target] || target != dying.VictimSeat ||
            root.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            context.OwnerSeat != root.OwnerSeat || _resolutionStack[index - 2] is not TurnEndingBoundaryFrame ending ||
            ending.Id != context.ParentFrameId || ending.TurnNumber != _turnNumber ||
            ending.OwnerSeat != context.SourceSeat || ending.OwnerSeat != context.TargetSeat ||
            ending.ItemIndex < 0 || ending.ItemIndex >= ending.Items.Count ||
            ending.Items[ending.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(root, candidate) ||
            !MatchesFrozenOwnOrPreviousEnding(candidate, context) ||
            context.Facts?.FrozenPreviousLivingSeat != ending.Items[ending.ItemIndex].Facts?.FrozenPreviousLivingSeat ||
            string.IsNullOrWhiteSpace(root.SkillInstanceId) ||
            _contentRegistry.Skills.GetValueOrDefault(root.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != root.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(root, definition);
        if (plan.Instructions.Count != 7 || root.InstructionIndex != plan.Instructions.Count ||
            plan.GetPausedInstruction(root.InstructionIndex).Effect is not
                { Op: SkillProgramEffectOp.LoseHpIfRevealedNonEquipmentDiffers, SourceBind: "drawn" } ||
            root.CardSetBindings.SingleOrDefault(b => b.Name == "drawn") is not
                { Visibility: SkillProgramCardSetVisibility.Public, CardIds: [var cardId], SourceLocations: [var from] } ||
            from != CardLocation.Hand(target)) return false;

        // These facts are produced in this exact order by the final instruction
        // and the existing one-point HP host. Source availability is deliberately
        // not re-tested after paying: losing the source cannot undo this cost.
        var history = NeighborDiscardHistory().ToArray();
        var reveal = history.OfType<ProgramCardsRevealedEvent>().LastOrDefault(e =>
            e.FrameId == root.Id && e.SkillId == root.SkillId && e.BindingId == root.TriggerId &&
            e.OwnerSeat == root.OwnerSeat && e.Bind == "drawn");
        var comparisons = history.OfType<RevealedCardHpComparedEvent>().Where(e => e.ProgramFrameId == root.Id).ToArray();
        var losses = history.OfType<ProgramSkillHpLostEvent>().Where(e => e.FrameId == root.Id).ToArray();
        if (reveal is not { Cards: [var shown] } || shown.Id != cardId || EquipmentCatalog.IsEquipment(shown.Kind) ||
            comparisons is not [var compared] || losses is not [var loss] ||
            compared.InstructionIndex != root.InstructionIndex - 1 || compared.GameplayHash != root.GameplayHash ||
            compared.Source != new CardConversionSource(root.SkillId, GetProgramBindingId(root), root.OwnerSeat, root.SkillInstanceId) ||
            compared.CardId != shown.Id || compared.RevealedKind != shown.Kind || compared.TargetSeat != target ||
            !compared.LosesHp || compared.TargetHp != 1 || compared.OwnerHp == compared.TargetHp ||
            loss.SkillId != root.SkillId || loss.TargetSeat != target || loss.Amount != 1 || loss.RemainingHp != 0 ||
            Array.IndexOf(history, reveal) >= Array.IndexOf(history, compared) ||
            Array.IndexOf(history, compared) >= Array.IndexOf(history, loss) ||
            !history.Skip(Array.IndexOf(history, loss) + 1).OfType<PlayerDyingEvent>().Any(e =>
                e.ResolutionId == dying.Id && e.VictimSeat == target && e.KillerSeat is null)) return false;

        if (index == _resolutionStack.Count - 1) return true;
        // These two mature proofs own the full actual rescue use and all of its
        // cost/card-window/HP/movement descendants; do not re-check their tail
        // as though a program Alcohol were a direct native Dying response.
        if (IsPaidHandRepaymentRescueRide(index, dying) || IsPaidHandRepaymentProgramAlcoholRide(index, dying)) return true;
        if (_resolutionStack[index + 1] is ProgramSkillFrame response)
        {
            return index + 1 == _resolutionStack.Count - 1 && response.WindowContext is { } responseContext &&
                responseContext.ParentFrameId == dying.Id && responseContext.TargetSeat == target &&
                responseContext.SourceSeat == dying.KillerSeat && responseContext.OwnerSeat == response.OwnerSeat &&
                (responseContext.Window == SkillProgramTriggerWindow.SelfDyingResponse && response.OwnerSeat == target ||
                 responseContext.Window == SkillProgramTriggerWindow.DyingResponse && response.OwnerSeat == dying.ResponderSeat) &&
                !string.IsNullOrWhiteSpace(response.SkillInstanceId) &&
                _contentRegistry.Skills.GetValueOrDefault(response.SkillId)?.Program is { } rescueDefinition &&
                rescueDefinition.GameplayHash == response.GameplayHash &&
                rescueDefinition.Triggers.Any(t => t.Id == response.TriggerId && t.Window == responseContext.Window);
        }
        for (var child = index + 1; child < _resolutionStack.Count; child++)
            if (!PreventionDrawObserverEdge(child)) return false;
        return true;
    }

    private sealed partial class ProgramSkillHost : IRevealedCardHpComparisonHost
    {
        public SkillProgramStepOutcome LoseHpIfRevealedNonEquipmentDiffers(ProgramSkillFrame frame, string sourceBind) =>
            engine.LoseHpIfRevealedNonEquipmentDiffers(frame, sourceBind);
    }
}
