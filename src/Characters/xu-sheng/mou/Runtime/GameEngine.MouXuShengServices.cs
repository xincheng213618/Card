namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouXuShengRuntime MouXuSheng => GetRuntimeModule<MouXuShengRuntime>();
    [ProgramCapability]
    private sealed class MouXuShengFacts(GameEngine e) : IMouXuShengFacts
    {
        public int Turn => e._turnNumber;
        public int TurnOwner => e._turnProgression.OwnerSeat;
        public long MovementSequence => e._movementSequence;
        public bool Finished => e._winner != Winner.None;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman;
        public string CardLabel(int id) => e.GetAdvancedCard(id).DisplayName;
        public bool Friendly(int owner, int other) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0, 0, 0, 1, 0, 0, false, false)) > 0;
        public int KeepValue(int owner, int id) => GetKeepValue(e.GetAdvancedCard(id), e._players[owner]);
        public SkillProgram? Program(string id) => e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public IReadOnlyList<MouXuShengCard> Hand(int seat) => Array.AsReadOnly(e.GetHand(e._players[seat]).Select(c => new MouXuShengCard(c.Id, seat, SuitColor(e.EffectiveSuit(e._players[seat], c)))).ToArray());
        public bool Discardable(int chooser, MouXuShengCard card) => e._cardZones.GetLocation(card.CardId) == CardLocation.Hand(card.Holder) &&
            !e.IsForeignEquipmentDiscardPrevented(chooser, e.GetAdvancedCard(card.CardId), CardLocation.Hand(card.Holder), OwnedCardMoveIntent.Discard);
        public CardLocation Location(int id) => e._cardZones.GetLocation(id);
        public MouXuShengUse? Use(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
        {
            if (context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.OwnerSeat != candidate.OwnerSeat || context.CardUse is not { } info ||
                e._resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window || window.Action.ActionId != info.CardActionId ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || window.Candidates[window.CandidateIndex] is not { } current ||
                current.OwnerSeat != candidate.OwnerSeat || current.SkillId != candidate.SkillId || current.TriggerId != candidate.BindingId ||
                current.SkillInstanceId != candidate.SkillInstanceId || current.GameplayHash != candidate.GameplayHash) return null;
            var use = e._resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Action?.ActionId == info.CardActionId);
            return new(window.Id, window.Action, use?.Id ?? 0);
        }
        public bool SourcedDamage(ProgramSkillWindowContext context, int owner) => context is
            { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, TargetSeat: var target, DamageFrameId: not null, Amount: > 0 } &&
            target == owner && e.IsValidPlayerSeat(source);
        public bool DamageParent(ProgramSkillFrame f)
        {
            if (f.SkillId != MouXuShengComposition.Yizhen || f.TriggerId != "mutual-private-hand" || f.WindowContext is not { } context || !SourcedDamage(context, f.OwnerSeat)) return false;
            var index = e._resolutionStack.FindIndex(x => x.Id == f.Id);
            return index > 0 && e._resolutionStack[index - 1] is DamageTriggerWindowFrame window && window.Id == context.ParentFrameId &&
                window.ParentFrameId == context.DamageFrameId && window.SourceSeat == context.SourceSeat && window.TargetSeat == f.OwnerSeat &&
                window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
                MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate());
        }
    }
    [ProgramCapability]
    private sealed class MouXuShengNative(GameEngine e) : IMouXuShengNative
    {
        public void PublishState() => e.AdvanceRulesAndPublishState();
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) ||
            e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Boost(ProgramSkillFrame f)
        {
            if (!e.MouXuSheng.Valid(f) || f.MouXuSheng is not { Accepted: true, Operation: SkillProgramEffectOp.ResolveMouXuShengQinqiang } r)
                throw new InvalidOperationException("Qinqiang requires its accepted original true Hand Use.");
            // A true response Use owns its response window, never the incoming
            // card's damage. Such a non-damaging card may still spend this item.
            if (r.UseId == 0) return;
            if (e.LifecycleCardUse(r.UseId) is not { Action: { } action } use || action.ActionId != r.ActionId || action.ActorSeat != f.OwnerSeat)
                throw new InvalidOperationException("Qinqiang must attach only to its own accepted card.");
            var boost = new MouXuShengBoost(f.Id, r.Source, f.GameplayHash, r.UseId, r.ActionId, r.X);
            e.UpdateLifecycleCardUse(use.Id, u => u with { MouXuShengBoosts = (u.MouXuShengBoosts ?? []).Append(boost).ToArray() });
            e.AdvanceEventRulesAndQueueFact(new MouXuShengBoostIssuedEvent(boost));
        }
        public void Discard(ProgramSkillFrame f)
        {
            if (!e.MouXuSheng.Valid(f) || f.MouXuSheng is not { Stage: MouXuShengStage.DiscardChildren, Paying: { } selected } r ||
                !new MouXuShengFacts(e).Discardable(selected.Chooser, selected.Card)) throw new InvalidOperationException("Yizhen requires the selected opposing real Hand entity.");
            var card = selected.Card with { IsRed = SuitColor(e.EffectiveSuit(e._players[selected.Card.Holder], e.GetAdvancedCard(selected.Card.CardId))) };
            e.MoveProgramLogicalDiscard(f, selected.Chooser, [card.CardId], new(MouXuShengRuntime.DiscardReason), (_, records) =>
            {
                var parent = e._resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(x => x.Id == f.Id);
                if (parent?.MouXuSheng is not { Stage: MouXuShengStage.DiscardChildren } paid || !e.MouXuSheng.Valid(parent) || paid.Paying != selected ||
                    records is not [var moved] || !e._cardMovements.Contains(moved) || moved.CardId != card.CardId || moved.From != CardLocation.Hand(card.Holder) ||
                    moved.Sequence <= paid.Before || moved.Reason.Value != MouXuShengRuntime.DiscardReason || moved.To != CardLocation.DiscardPile && moved.To != CardLocation.OutsideGame)
                    throw new InvalidOperationException("Yizhen's primary discard must retain its chooser, original holder and exact material.");
                e.ReplaceRuntimeFrame(f.Id, parent with { MouXuSheng = paid with { After = moved.Sequence, Paid = paid.Paid.Append(new(selected.Chooser, card, moved.Sequence)).ToArray() } });
                e.AdvanceEventRulesAndQueueFact(new MouXuShengDiscardPaidEvent(f.Id, selected.Chooser, card.Holder, card.CardId, card.IsRed, moved.Sequence));
            });
        }
        public void Claim(ProgramSkillFrame f, IReadOnlyList<int> ids)
        {
            if (!e.MouXuSheng.Valid(f) || f.MouXuSheng is not { Stage: MouXuShengStage.ClaimChildren, Paid: [var first, var second] } r ||
                first.Card.IsRed is not { } color || second.Card.IsRed != color || ids.Any(id => !r.Paid.Any(p => p.Card.CardId == id) || e._cardZones.GetLocation(id) != CardLocation.DiscardPile))
                throw new InvalidOperationException("Yizhen may only recover its matching-color actual discarded entities still in the discard pile.");
            e.MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(f.OwnerSeat), new(MouXuShengRuntime.ClaimReason), (_, records) =>
            {
                var parent = e._resolutionStack.OfType<ProgramSkillFrame>().Single(x => x.Id == f.Id);
                e.ReplaceRuntimeFrame(f.Id, parent with { MouXuSheng = parent.MouXuSheng! with { After = records.Max(m => m.Sequence) } });
                foreach (var moved in records.Where(m => m.To == CardLocation.Hand(f.OwnerSeat)))
                    e.AdvanceEventRulesAndQueueFact(new MouXuShengClaimedEvent(f.Id, moved.CardId, moved.Sequence));
            }, disclosure: CardMovementDisclosure.FaceUp);
        }
    }
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 34900)]
    private int MouXuShengDamageBonus(IDamageAttempt attack)
    {
        if (attack.IsChainPropagation || attack is not CardAttackHandle || LifecycleCardUse(attack.ResolutionId) is not { Action: { } action, MouXuShengBoosts: { } boosts } use) return 0;
        return boosts.Where(b => b.UseId == use.Id && b.ActionId == action.ActionId && b.Source.OwnerSeat == action.ActorSeat &&
            _contentRegistry.GetSkill(b.Source.SkillId).Program?.GameplayHash == b.GameplayHash &&
            ProgramEventHistory<MouXuShengBoostIssuedEvent>().Count(e => e.Boost == b) == 1 &&
            ProgramEventHistory<MouXuShengBenefitUsedEvent>().Count(e => e.FrameId == b.FrameId && e.ActionId == b.ActionId && e.Damage && e.X == b.X) == 1).Sum(b => b.X);
    }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 34900)]
    private CardSnapshot[] MouXuShengPrivateHandViews(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.MouXuSheng is { Operation: SkillProgramEffectOp.ResolveMouXuShengYizhen } && MouXuSheng.Valid(f))
        .SelectMany(f => f.MouXuSheng!.Views.Where(v => v.Viewer == viewer).SelectMany(v => v.Cards))
        .Where(c => _cardZones.GetLocation(c.CardId) == CardLocation.Hand(c.Holder)).DistinctBy(c => c.CardId).Select(c => ToSnapshot(GetAdvancedCard(c.CardId))).ToArray();
}
