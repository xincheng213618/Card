namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksNativeDrawOrdinals => _contentRegistry.ProgramDependencies
        .UsesTriggerCondition(SkillProgramTriggerConditionKind.NativeDrawActualTurnOrdinalIs);

    private static bool UsesNativeDrawOrdinal(SkillProgramTrigger trigger) => ProgramInstructionResolver.Default
        .Features(trigger).UsesCondition(SkillProgramTriggerConditionKind.NativeDrawActualTurnOrdinalIs);

    // The event prefix belongs to this invoice, not to whichever turn or draw
    // happens to be current when an older gain child finally returns.
    private static int? NativeDrawOrdinalAt(IReadOnlyList<IGameEvent> history, int endExclusive,
        int owner, int turn, int turnOwner)
    {
        if (turn < 1) return null;
        var startIndex = endExclusive - 1;
        while (startIndex >= 0 && history[startIndex] is not TurnStartedEvent) startIndex--;
        if (startIndex < 0) return null;
        var start = (TurnStartedEvent)history[startIndex];
        for (var index = startIndex + 1; index < endExclusive; index++)
            if (history[index] is TurnEndedEvent ended && ended.TurnNumber == start.TurnNumber && ended.ActorSeat == start.ActorSeat)
                return null;
        if (start.TurnNumber != turn || start.ActorSeat != turnOwner)
            throw new InvalidOperationException("A native draw ordinal lost its original actual-turn boundary.");
        var seen = new HashSet<long>();
        var ordinal = 1;
        for (var index = startIndex + 1; index < endExclusive; index++)
        {
            if (history[index] is not NativeDrawInvocationRecordedEvent draw || draw.OwnerSeat != owner) continue;
            if (draw.InvocationId <= 0 || draw.ActualCount < 1 || draw.Requested < draw.ActualCount ||
                draw.TurnNumber != turn || draw.ActualTurnOwnerSeat != turnOwner || draw.Reason == CardMoveReasons.InitialDeal ||
                !seen.Add(draw.InvocationId))
                throw new InvalidOperationException("An actual-turn draw prefix repeated or changed an issued native invoice.");
            ordinal = checked(ordinal + 1);
        }
        return ordinal;
    }

    private IReadOnlyList<NativeDrawOrdinalEntitlement>? CaptureNativeDrawOrdinalEntitlements(
        long invocationId, int ownerSeat, long before)
    {
        if (!TracksNativeDrawOrdinals) return null;
        var history = CompleteProgramEventHistory().ToArray();
        if (NativeDrawOrdinalAt(history, history.Length, ownerSeat, _turnNumber, _turnProgression.OwnerSeat) is null)
            return null;
        var owner = _players[ownerSeat];
        var sources = owner.IsAlive
            ? GetSkillBindingShard(owner).GetInstanceTriggers(SkillProgramTriggerWindow.CardsGained)
                .Where(binding => UsesNativeDrawOrdinal(binding.Trigger) &&
                    HasRuntimeSkillInstance(owner, binding.SkillId, binding.SkillInstanceId))
                .Select(binding => new NativeDrawOrdinalEntitlement(
                    new(binding.SkillId, binding.Trigger.Id, ownerSeat, binding.SkillInstanceId), binding.Program.GameplayHash))
                .Distinct().OrderBy(source => source.Source.SkillId, StringComparer.Ordinal)
                .ThenBy(source => source.Source.BindingId, StringComparer.Ordinal)
                .ThenBy(source => source.Source.SkillInstanceId, StringComparer.Ordinal).ToArray()
            : [];
        AdvanceEventRulesAndQueueFact(new NativeDrawOrdinalEntitlementsCapturedEvent(invocationId, ownerSeat,
            _turnNumber, _turnProgression.OwnerSeat, before, sources.Length));
        foreach (var source in sources)
            AdvanceEventRulesAndQueueFact(new NativeDrawOrdinalSourceCapturedEvent(invocationId, ownerSeat, before, source));
        return Array.AsReadOnly(sources);
    }

    private int? FreezeNativeDrawOrdinal(NativeDrawInvocationDraft draft)
    {
        if (draft.OrdinalEntitlements is null) return null;
        var history = CompleteProgramEventHistory().ToArray();
        return NativeDrawOrdinalAt(history, history.Length, draft.Owner, draft.Turn, draft.TurnOwner) ??
            throw new InvalidOperationException("A native draw ended outside its captured actual turn.");
    }

    private bool ValidNativeDrawOrdinalProof(NativeDrawInvocationProof proof)
    {
        if (!TracksNativeDrawOrdinals)
            return proof.ActualTurnDrawOrdinal is null && proof.OrdinalEntitlements is null;
        var history = CompleteProgramEventHistory().ToArray();
        var invoiceIndexes = history.Select((fact, index) => (fact, index))
            .Where(item => item.fact is NativeDrawInvocationRecordedEvent draw && draw.InvocationId == proof.InvocationId).ToArray();
        if (invoiceIndexes is not [var entry]) return false;
        var fact = (NativeDrawInvocationRecordedEvent)entry.fact;
        var expected = NativeDrawOrdinalAt(history, entry.index, proof.OwnerSeat, proof.ActualTurnNumber, proof.ActualTurnOwnerSeat);
        var summaries = history.Select((payload, index) => (payload, index))
            .Where(item => item.payload is NativeDrawOrdinalEntitlementsCapturedEvent summaryFact && summaryFact.InvocationId == proof.InvocationId).ToArray();
        var sources = history.Select((payload, index) => (payload, index))
            .Where(item => item.payload is NativeDrawOrdinalSourceCapturedEvent sourceFact && sourceFact.InvocationId == proof.InvocationId).ToArray();
        if (expected is null)
            return proof.ActualTurnDrawOrdinal is null && fact.ActualTurnDrawOrdinal is null && proof.OrdinalEntitlements is null &&
                summaries.Length == 0 && sources.Length == 0;
        if (proof.ActualTurnDrawOrdinal != expected || fact.ActualTurnDrawOrdinal != expected ||
            proof.OrdinalEntitlements is not { } entitlements || entitlements is not System.Collections.IList { IsReadOnly: true } ||
            summaries is not [var summary] || summary.index >= entry.index ||
            summary.payload is not NativeDrawOrdinalEntitlementsCapturedEvent captured ||
            captured.OwnerSeat != proof.OwnerSeat || captured.ActualTurnNumber != proof.ActualTurnNumber ||
            captured.ActualTurnOwnerSeat != proof.ActualTurnOwnerSeat || captured.SequenceBefore != proof.SequenceBefore ||
            captured.Count != entitlements.Count || sources.Length != entitlements.Count || entitlements.Distinct().Count() != entitlements.Count)
            return false;
        foreach (var entitlement in entitlements)
        {
            if (entitlement is null || entitlement.Source is not { } source || source.OwnerSeat != proof.OwnerSeat ||
                !_contentRegistry.Skills.TryGetValue(source.SkillId, out var definition) || definition.Program is not { } program ||
                program.GameplayHash != entitlement.GameplayHash ||
                program.Triggers.SingleOrDefault(binding => binding.Id == source.BindingId) is not { } trigger || !UsesNativeDrawOrdinal(trigger) ||
                sources.Count(item => item.index > summary.index && item.index < entry.index &&
                    item.payload is NativeDrawOrdinalSourceCapturedEvent issued && issued.OwnerSeat == proof.OwnerSeat &&
                    issued.SequenceBefore == proof.SequenceBefore && issued.Entitlement == entitlement) != 1)
                return false;
        }
        return true;
    }

    private int[] MatchingNativeDrawOrdinalIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate)
    {
        if (batch.NativeDrawInvocation is not { ActualTurnDrawOrdinal: > 0, OrdinalEntitlements: { } sources } proof ||
            proof.OwnerSeat != candidate.OwnerSeat || proof.LastBatchId != batch.Id || proof.ParentFrameId != batch.ParentFrameId ||
            proof.ActualTurnNumber != batch.TurnNumber || !ValidNativeDrawInvocation(proof) ||
            !sources.Contains(new(new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId), candidate.GameplayHash)))
            return [];
        var last = proof.Materials[^1];
        return batch.Movements.Select((movement, index) => (movement, index)).Where(item =>
            item.movement.Sequence == last.MovementSequence && item.movement.CardId == last.CardId &&
            item.movement.CardKind == last.PrintedKind && item.movement.From == CardLocation.DrawPile &&
            item.movement.To == CardLocation.Hand(candidate.OwnerSeat) && item.movement.Reason == proof.Reason &&
            _cardMovements.Contains(item.movement)).Select(item => item.index).ToArray();
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 27910)]
    private bool CanRunNativeDrawOrdinalTrigger(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        if (!UsesNativeDrawOrdinal(trigger)) return true;
        return context.Window == SkillProgramTriggerWindow.CardsGained && context.MovementBatch is { } batch &&
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(frame => frame.Id == context.ParentFrameId) is { } parent &&
            parent.Batch.Id == batch.Id && parent.Batch.Movements.SequenceEqual(batch.Movements) &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == candidate &&
            MatchingNativeDrawOrdinalIndexes(batch, candidate).Length == 1 &&
            context.Facts?.NativeDrawActualTurnOrdinal == batch.NativeDrawInvocation!.ActualTurnDrawOrdinal;
    }
}
