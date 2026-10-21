namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouHuLieRuntime MouHuLieRules => GetRuntimeModule<MouHuLieRuntime>();
    private bool HasMouHuLie => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.StartMouHuLieChuanyu);
    private MouHuLieYu? CurrentMouHuLieYu(int owner, string instance) => ProgramEventHistory<MouHuLieMarkedEvent>().Select(e => e.Yu).LastOrDefault(y => y.Source.OwnerSeat == owner && y.Source.SkillInstanceId == instance && y.Round == _roundNumber && _contentRegistry.GetSkill(y.Source.SkillId).Program?.GameplayHash == y.GameplayHash && !ProgramEventHistory<MouHuLieClearedEvent>().Any(e => e.MarkId == y.MarkId));
    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 37500)] private string? MouHuLieCardLabel(int cardId) => HasMouHuLie && ProgramEventHistory<MouHuLieMarkedEvent>().Any(e => e.Yu.CardId == cardId && CurrentMouHuLieYu(e.Yu.Source.OwnerSeat, e.Yu.Source.SkillInstanceId)?.MarkId == e.Yu.MarkId) ? "舆" : null;
    [ProgramPipelineHook(ProgramPipelinePhase.IncludeTriggerCandidate, 37500)] private IReadOnlyList<ProgramTriggerCandidate> IncludeMouHuLieRoundDebt(CharacterState owner, SkillProgramTriggerWindow window, IReadOnlyList<ProgramTriggerCandidate> ordinary)
    {
        if (!HasMouHuLie || window != SkillProgramTriggerWindow.RoundEnding || !owner.IsAlive) return ordinary;
        var result = ordinary.ToList();
        foreach (var mark in ProgramEventHistory<MouHuLieMarkedEvent>().Select(e => e.Yu).Where(y => y.Source.OwnerSeat == owner.Seat && CurrentMouHuLieYu(owner.Seat, y.Source.SkillInstanceId)?.MarkId == y.MarkId))
        {
            var candidate = new ProgramTriggerCandidate(owner.Seat, MouHuLieRuntime.Chuanyu, "round-clear-and-slashes", mark.Source.SkillInstanceId, mark.GameplayHash, 0);
            if (!result.Contains(candidate)) result.Add(candidate);
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void CaptureMouHuLieUsedDiscard(CardMovementBatchContext batch)
    {
        if (!HasMouHuLie || batch.ParentFrameId is not { } parent) return;
        foreach (var yu in ProgramEventHistory<MouHuLieMarkedEvent>().Select(e => e.Yu).Where(y => CurrentMouHuLieYu(y.Source.OwnerSeat, y.Source.SkillInstanceId)?.MarkId == y.MarkId))
            foreach (var move in batch.Movements.Where(m => m.CardId == yu.CardId && IsNativeTrueUseDiscardMovement(batch, m)))
            {
                var fact = new MouHuLieUsedDiscardEvent(yu, batch.Id, parent, batch.TurnNumber, move.CardId, move.Sequence);
                if (!ProgramEventHistory<MouHuLieUsedDiscardEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
            }
    }
    private bool IsMouHuLieUsedDiscard(CardMovementBatchContext batch, CardMovementRecord move, MouHuLieYu yu) => batch.ParentFrameId is { } parent && move.CardId == yu.CardId && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile && batch.Movements.Contains(move) && _cardZones.GetLocation(move.CardId) == CardLocation.DiscardPile && _cardMovements.LastOrDefault(m => m.CardId == move.CardId) == move && ProgramEventHistory<MouHuLieUsedDiscardEvent>().Count(e => e == new MouHuLieUsedDiscardEvent(yu, batch.Id, parent, batch.TurnNumber, move.CardId, move.Sequence)) == 1;
    [ProgramPipelineHook(ProgramPipelinePhase.SelectDiscardBatchIndexes, 37500)] private int[]? MouHuLieUsedIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate, ProgramInstructionFeatures features) => !features.HasOperation(SkillProgramEffectOp.RegiveMouHuLieChuanyu) ? null : CurrentMouHuLieYu(candidate.OwnerSeat, candidate.SkillInstanceId) is { } yu ? batch.Movements.Select((m, i) => (m, i)).Where(x => IsMouHuLieUsedDiscard(batch, x.m, yu)).Select(x => x.i).ToArray() : [];
    [ProgramCapability] private sealed class MouHuLieFacts(GameEngine e) : IMouHuLieFacts
    {
        public int Round => e._roundNumber; public int Turn => e._turnNumber; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int s) => e.IsValidPlayerSeat(s) && e._players[s].IsAlive; public bool Human(int s) => e._players[s].IsHuman; public string Name(int s) => e._players[s].Name; public int HandCount(int s) => e.GetHand(e._players[s]).Count;
        public int Keep(int actor, int id) => GetKeepValue(e.GetAdvancedCard(id), e._players[actor]); public bool Friendly(int actor, int target) => actor == target || (e._aiBrains.TryGetValue(actor, out var brain) ? brain.ScoreProgramTarget(e.CreateSnapshot(actor), target, new(0, 0, 0, 1, 0, 0, false, false)) > 0 : e._players[actor].TeamRevealed && e._players[target].TeamRevealed && e._players[actor].TeamId is { } t && e._players[target].TeamId == t);
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!; public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public IReadOnlyList<int> Living() => Array.AsReadOnly(e._players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray()); public IReadOnlyList<int> ActionOrder(IEnumerable<int> seats) => Array.AsReadOnly(seats.Distinct().OrderBy(s => (s - e._currentSeat + e._players.Count) % e._players.Count).ToArray());
        public IReadOnlyList<MouHuLieCard> Owned(int owner, bool handOnly) => !Alive(owner) ? [] : Array.AsReadOnly((handOnly ? new[] { CardZoneKind.Hand } : new[] { CardZoneKind.Hand, CardZoneKind.Equipment }).SelectMany(z => e._cardZones.CardsAt(new(z, owner)).Where(c => !e.IsSelfHandCategoryDiscardForbidden(owner, c, new(z, owner), OwnedCardMoveIntent.Transfer)).Select(c => new MouHuLieCard(c.Id, c.Kind, new(z, owner), c.DisplayName))).ToArray());
        public MouHuLieCard Current(int id) { var c = e.GetAdvancedCard(id); return new(id, c.Kind, e._cardZones.GetLocation(id), c.DisplayName); }
        public MouHuLieYu? ActiveYu(int owner, string instance) => e.CurrentMouHuLieYu(owner, instance);
        public MouHuLieYu? UsedYu(ProgramSkillWindowContext w, int owner, string instance)
        {
            if (w.Window != SkillProgramTriggerWindow.DiscardPileReceived || w.MovementBatch is not { } b || w.MovementIndex is not { } i || i < 0 || i >= b.Movements.Count || ActiveYu(owner, instance) is not { } yu) return null;
            var m = b.Movements[i]; return e.IsMouHuLieUsedDiscard(b, m, yu) ? yu : null;
        }
        public int? YitouTarget(ProgramSkillWindowContext w, int owner)
        {
            if (w.Window != SkillProgramTriggerWindow.PlayPhaseStarting || w.TargetSeat is not { } target || target == owner || !Alive(target) || e._currentSeat != target || e.GetHand(e._players[target]).Count != e._players.Where(p => p.IsAlive).Max(p => e.GetHand(p).Count)) return null;
            return target;
        }
        public MouHuLieDamageEvent? Damage(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => e.MouHuLieDamageReceipt(c, w);
        public bool CanSlash(ProgramSkillFrame f) => e.CanMouHuLieSlash(f);
        public bool Parent(ProgramSkillFrame f)
        {
            if (f.TriggerId != f.ActivationId || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat) return false;
            var at = e._resolutionStack.FindIndex(x => x.Id == f.Id); if (at < 1 || e._resolutionStack[at - 1].Id != w.ParentFrameId) return false;
            var candidate = e._resolutionStack[at - 1] switch {
                ProgramLifecycleTriggerWindowFrame p when p.Window == w.Window && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => p.Candidates[p.CandidateIndex],
                PlayPhaseStartingBoundaryFrame p when p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count => p.Items[p.ItemIndex].Candidate,
                CardsMovedTriggerWindowFrame p when p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => p.Candidates[p.CandidateIndex], _ => null };
            if (candidate is not null) return MountObserverCandidateMatches(f, candidate);
            return w.Window == SkillProgramTriggerWindow.AfterDamageApplied && Damage(new(f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, 0, w.OccurrenceIndex), w) is not null;
        }
    }
    [ProgramCapability] private sealed class MouHuLieNative(GameEngine e) : IMouHuLieNative
    {
        public void Publish() => e.AdvanceRulesAndPublishState(); public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Use(ProgramSkillFrame f) => e.IssueMouHuLieSlash(f);
        public void Give(ProgramSkillFrame f)
        {
            if (!e.MouHuLieRules.Valid(f) || f.MouHuLie is not { Stage: MouHuLieStage.GiveChildren } r || !e._players[r.Target].IsAlive) throw new InvalidOperationException("Chuanyu/Yitou needs its accepted real recipient.");
            var initial = r.Operation == SkillProgramEffectOp.StartMouHuLieChuanyu;
            if (initial && r.Target == f.OwnerSeat)
            {
                if (r.Cards is not [var card] || !new MouHuLieFacts(e).Owned(f.OwnerSeat, false).Contains(card)) throw new InvalidOperationException("Self Chuanyu marks only its exact unchanged own HE entity.");
                e.MouHuLieRules.Given(f.Id, [], true); return;
            }
            if (r.Cards.Count == 0 || r.Cards.Any(c => e._cardZones.GetLocation(c.Id) != c.From) || (initial || r.Operation == SkillProgramEffectOp.ResolveMouHuLieYitou) && r.Cards.Any(c => !new MouHuLieFacts(e).Owned(f.OwnerSeat, r.Operation == SkillProgramEffectOp.ResolveMouHuLieYitou).Contains(c)) || r.Operation == SkillProgramEffectOp.RegiveMouHuLieChuanyu && (r.Yu is null || r.Cards is not [var used] || used.Id != r.Yu.CardId || used.From != CardLocation.DiscardPile)) throw new InvalidOperationException("Chuanyu/Yitou lost its exact current physical Give material.");
            e.MoveProgramCardsFromMultipleSources(r.Cards.Select(c => c.Id).ToArray(), CardLocation.Hand(r.Target), new("skill-program.mou:chuanyu-or-yitou.give"), (_, moves) => {
                e.MouHuLieRules.Given(f.Id, moves);
                if (r.Cards.All(c => c.From.OwnerSeat == f.OwnerSeat) && r.Target != f.OwnerSeat) e.RecordNativeGivenCards(f.Id, f.OwnerSeat, r.Target, r.Cards.Select(c => c.Id).ToArray(), r.Before);
            }, disclosure: CardMovementDisclosure.FaceUp);
        }
        public void Clear(ProgramSkillFrame f, MouHuLieCard card)
        {
            if (!e.MouHuLieRules.Valid(f) || f.MouHuLie is not { Stage: MouHuLieStage.ClearChildren } r || r.Yu?.CardId != card.Id || r.Index < 1 || r.Cards[r.Index - 1] != card || e._cardZones.GetLocation(card.Id) != card.From || card.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.DrawPile)) throw new InvalidOperationException("Chuanyu may discard only its own marked actual reachable entity.");
            void Paid(long batch, IReadOnlyList<CardMovementRecord> moves) => e.MouHuLieRules.ClearedMoves(f.Id, moves);
            if (card.From.OwnerSeat is { } holder) e.MoveProgramLogicalDiscard(f, holder, [card.Id], new("skill-program.mou:chuanyu.round-discard"), Paid);
            else e.MoveProgramCardsFromMultipleSources([card.Id], CardLocation.DiscardPile, new("skill-program.mou:chuanyu.round-deck-discard"), Paid, disclosure: CardMovementDisclosure.FaceUp);
        }
    }
}
