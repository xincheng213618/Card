namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramOwnedHandRankSumGiftReceipt? CaptureOwnedHandRankSumGift(ProgramSkillFrame frame, IReadOnlyList<int> ids, CardMoveReason reason)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var required = plan.Instructions[0].ExactRankSum!.Value;
        var entities = ids.Select(id =>
        {
            var card = GetAdvancedCard(id);
            return new ProgramOwnedHandRankSumGiftEntity(id, card.Kind, card.Rank, _cardZones.GetLocation(id),
                JuewuForeignHandRank(_players[frame.OwnerSeat], card) ?? card.Rank);
        }).ToArray();
        if (entities.All(e => e.EffectiveRank == e.PrintedRank)) return null;
        if (entities.Any(e => e.From != CardLocation.Hand(frame.OwnerSeat)) || entities.Sum(e => e.EffectiveRank) != required)
            throw new InvalidOperationException("The effective-rank gift lost its original Hand payment before issuance.");
        var source = new CardConversionSource(frame.SkillId, frame.ActivationId!, frame.OwnerSeat, frame.SkillInstanceId);
        var bind = plan.Instructions[0].ResultBind!;
        AdvanceEventRulesAndQueueFact(new OwnedHandRankSumGiftIssuedEvent(frame.Id, source, frame.GameplayHash, bind,
            frame.SelectedTargetSeats.Single(), required, entities.Length, _movementSequence, reason));
        for (var i = 0; i < entities.Length; i++)
        {
            var e = entities[i];
            AdvanceEventRulesAndQueueFact(new OwnedHandRankSumGiftEntityFrozenEvent(frame.Id, i, e.CardId,
                e.PrintedKind, e.PrintedRank, e.From, e.EffectiveRank));
        }
        return new(frame.Id, required, _movementSequence, reason) { Entities = entities };
    }

    private void SealOwnedHandRankSumGift(long frameId, long batchId, IReadOnlyList<CardMovementRecord> movements)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.BoundParticipantGift is not { RankSum: { } ranks } gift) return;
        if (ranks.BatchId != 0 || movements.Count != ranks.Entities.Count || movements.Any(m =>
            m.Sequence <= ranks.SequenceBefore || m.Reason != ranks.Reason || m.To != CardLocation.Hand(gift.RecipientSeat) ||
            !ranks.Entities.Any(e => e.CardId == m.CardId && e.PrintedKind == m.CardKind && e.From == m.From)))
            throw new InvalidOperationException("The effective-rank gift lost its one exact native atomic payment.");
        var after = movements.Max(m => m.Sequence);
        ReplaceRuntimeTop(frame with { BoundParticipantGift = gift with { RankSum = ranks with { BatchId = batchId, SequenceAfter = after } } });
        AdvanceEventRulesAndQueueFact(new OwnedHandRankSumGiftPaidEvent(frameId, batchId, after, movements.Count));
    }

    private bool ValidOwnedHandRankSumGift(ProgramSkillFrame frame, ProgramSkillCardSetBinding source, int required)
    {
        if (frame.BoundParticipantGift?.RankSum is not { } ranks)
            return source.CardIds.Sum(id => { var c = GetAdvancedCard(id); return JuewuForeignHandRank(_players[frame.OwnerSeat], c) ?? c.Rank; }) == required;
        var gift = frame.BoundParticipantGift;
        if (ranks.OwnerFrameId != frame.Id || ranks.RequiredSum != required || ranks.SequenceBefore < 0 ||
            ranks.BatchId <= frame.Id || ranks.SequenceAfter <= ranks.SequenceBefore || ranks.SequenceAfter > _movementSequence ||
            ranks.Entities is not System.Collections.IList { IsReadOnly: true } ||
            !ranks.Entities.Select(e => e.CardId).SequenceEqual(source.CardIds) || ranks.Entities.Sum(e => e.EffectiveRank) != required ||
            ranks.Entities.Any(e => e.From != CardLocation.Hand(frame.OwnerSeat) || e.EffectiveRank is < 1 or > 13 ||
                GetAdvancedCard(e.CardId) is not { } c || c.Kind != e.PrintedKind || c.Rank != e.PrintedRank) ||
            ProgramEventHistory<OwnedHandRankSumGiftIssuedEvent>().Where(e => e.FrameId == frame.Id).ToArray() is not [var issued] ||
            issued.Source != new CardConversionSource(frame.SkillId, frame.ActivationId!, frame.OwnerSeat, frame.SkillInstanceId) ||
            issued.GameplayHash != frame.GameplayHash || issued.SourceBind != gift.SourceBind || issued.RecipientSeat != gift.RecipientSeat ||
            issued.RequiredSum != required || issued.EntityCount != ranks.Entities.Count || issued.SequenceBefore != ranks.SequenceBefore || issued.Reason != ranks.Reason ||
            ProgramEventHistory<OwnedHandRankSumGiftPaidEvent>().Where(e => e.FrameId == frame.Id).ToArray() is not [var paid] ||
            paid.BatchId != ranks.BatchId || paid.SequenceAfter != ranks.SequenceAfter || paid.EntityCount != ranks.Entities.Count) return false;
        var facts = ProgramEventHistory<OwnedHandRankSumGiftEntityFrozenEvent>().Where(e => e.FrameId == frame.Id).OrderBy(e => e.Index).ToArray();
        if (facts.Length != ranks.Entities.Count) return false;
        for (var i = 0; i < facts.Length; i++)
        {
            var e = ranks.Entities[i]; var fact = facts[i];
            if (fact.Index != i || fact.CardId != e.CardId || fact.PrintedKind != e.PrintedKind || fact.PrintedRank != e.PrintedRank ||
                fact.From != e.From || fact.EffectiveRank != e.EffectiveRank || _cardMovements.Count(m =>
                    m.Sequence > ranks.SequenceBefore && m.Sequence <= ranks.SequenceAfter && m.CardId == e.CardId &&
                    m.CardKind == e.PrintedKind && m.From == e.From && m.To == CardLocation.Hand(gift.RecipientSeat) && m.Reason == ranks.Reason) != 1) return false;
        }
        return true;
    }

    private void ValidateOwnedHandRankSumGiftActivation(ProgramSkillFrame frame)
    {
        var program = _contentRegistry.GetSkill(frame.SkillId).Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        var activation = plan.Activation;
        var move = plan.GetPausedInstruction(frame.InstructionIndex).Effect;
        if (frame.TriggerId is not null || frame.WindowContext is not null ||
            program.GameplayHash != frame.GameplayHash || activation is null ||
            activation.Id != frame.ActivationId || activation.MinCards != 0 || activation.MaxCards != 0 ||
            activation.MinTargets != 1 || activation.MaxTargets != 1 ||
            activation.TargetKind != SkillProgramTargetKind.OtherLiving || frame.SelectedCardIds.Count != 0 ||
            frame.SelectedTargetSeats is not [var recipient] || recipient == frame.OwnerSeat ||
            frame.InstructionIndex != 2 || plan.Instructions[0] is not
                { Op: SkillProgramEffectOp.SelectOwnedHandRankSum, ResultBind: { } bind, ExactRankSum: { } rankSum } ||
            move is not { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner,
                Destination: SkillProgramCardDestination.SelectedTargetHand, AwaitMovementTriggers: true,
                Condition.Kind: SkillProgramConditionKind.Always, ExceptBind: null } || move.SourceBind != bind ||
            CompleteProgramEventHistory().OfType<ProgramSkillStartedEvent>().Count(fact =>
                fact.FrameId == frame.Id && fact.OwnerSeat == frame.OwnerSeat && fact.SkillId == frame.SkillId &&
                fact.ActivationId == frame.ActivationId) != 1)
            throw new InvalidOperationException("An awaited rank-sum gift lost its exact original activation, target or paused movement.");
        var source = GetProgramCardSet(frame, bind);
        if (source.CardIds.Count == 0 || source.CardIds.Count > rankSum ||
            source.Visibility != SkillProgramCardSetVisibility.Private ||
            source.SelectionActorSeat is { } actor && actor != frame.OwnerSeat ||
            source.CardIds.Distinct().Count() != source.CardIds.Count ||
            source.SourceLocations.Count != source.CardIds.Count ||
            source.SourceLocations.Any(location => location != CardLocation.Hand(frame.OwnerSeat)) ||
            !ValidOwnedHandRankSumGift(frame, source, rankSum))
            throw new InvalidOperationException("An awaited rank-sum gift lost its private frozen owner-hand cost.");
    }
}
