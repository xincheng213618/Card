namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouCaoHongRuntime MouCaoHong => GetRuntimeModule<MouCaoHongRuntime>();
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 35900, SkillProgramEffectOp.ResolveMouCaoHongXianju)]
    private bool MouCaoHongCanActivate(ProgramActivationLegalityContext c) => c.SkillId == MouCaoHongComposition.Xianju && new MouCaoHongFacts(this).ActualPlay(c.OwnerSeat) &&
        _players[c.OwnerSeat].IsAlive && new MouCaoHongFacts(this).InRange(c.OwnerSeat).Any(s => new MouCaoHongFacts(this).Cards(c.OwnerSeat, s, false).Count > 0);
    private bool ValidMouCaoHongDebt(MouCaoHongXianjuIssuedEvent d) => d.Source.SkillId == MouCaoHongComposition.Xianju && d.Source.BindingId == "obtain-in-range" && d.TurnOwner == d.Source.OwnerSeat && d.Turn > 0 && d.PhaseId > 0 &&
        _contentRegistry.GetSkill(d.Source.SkillId).Program?.GameplayHash == d.GameplayHash && ProgramEventHistory<MouCaoHongStartedEvent>().Count(s => s.FrameId == d.FrameId && s.Source == d.Source && s.GameplayHash == d.GameplayHash &&
            s.Operation == SkillProgramEffectOp.ResolveMouCaoHongXianju && s.Turn == d.Turn && s.TurnOwner == d.TurnOwner && s.PhaseId == d.PhaseId) == 1 &&
        ProgramEventHistory<ProgramSkillStartedEvent>().Count(s => s.FrameId == d.FrameId && s.SkillId == d.Source.SkillId && s.OwnerSeat == d.Source.OwnerSeat && s.ActivationId == d.Source.BindingId) == 1;
    private IEnumerable<ProgramTriggerCandidate> IssuedMouCaoHongPlayEndingCandidates(CharacterState owner)
    {
        if (!owner.IsAlive || _winner != Winner.None) yield break; var all = ProgramEventHistory<MouCaoHongXianjuIssuedEvent>().ToArray();
        for (var i = 0; i < all.Length; i++)
        {
            var d = all[i]; if (d.Source.OwnerSeat != owner.Seat || d.Turn != _turnNumber || d.TurnOwner != _currentSeat || d.PhaseId != _cardUseDebitPhaseInstanceId || !ValidMouCaoHongDebt(d) || ProgramEventHistory<MouCaoHongDebtConsumedEvent>().Any(e => e.DebtId == d.FrameId)) continue;
            var t = _contentRegistry.GetSkill(d.Source.SkillId).Program!.Triggers.Single(t => t.Id == "issued-play-end"); yield return new(owner.Seat, d.Source.SkillId, t.Id, d.Source.SkillInstanceId, d.GameplayHash, t.Priority, i);
        }
    }
    private MouCaoHongXianjuIssuedEvent? CurrentMouCaoHongDebt(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (c.SkillId != MouCaoHongComposition.Xianju || c.BindingId != "issued-play-end" || w.Window != SkillProgramTriggerWindow.PlayEnding || w.OwnerSeat != c.OwnerSeat || c.OwnerSeat != _currentSeat ||
            w.OccurrenceIndex != c.OccurrenceIndex || _phase != TurnPhase.Play || _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.PlayEnding } p ||
            p.OwnerSeat != c.OwnerSeat || p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count || p.Candidates[p.CandidateIndex] != c) return null;
        var all = ProgramEventHistory<MouCaoHongXianjuIssuedEvent>().ToArray(); if (c.OccurrenceIndex < 0 || c.OccurrenceIndex >= all.Length) return null; var d = all[c.OccurrenceIndex];
        if (!ValidMouCaoHongDebt(d) || d.Source.OwnerSeat != c.OwnerSeat || d.Source.SkillInstanceId != c.SkillInstanceId || d.GameplayHash != c.GameplayHash || d.Turn != _turnNumber || d.TurnOwner != _currentSeat || d.PhaseId != _cardUseDebitPhaseInstanceId) return null;
        return !ProgramEventHistory<MouCaoHongDebtConsumedEvent>().Any(e => e.DebtId == d.FrameId) || _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MouCaoHong is { } r && r.DebtId == d.FrameId && MountObserverCandidateMatches(f, c) && f.WindowContext == w) ? d : null;
    }
    private bool IsIssuedMouCaoHongCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => _winner == Winner.None && IsValidPlayerSeat(c.OwnerSeat) && _players[c.OwnerSeat].IsAlive && CurrentMouCaoHongDebt(c, w) is not null;
    [ProgramDistanceRule(ProgramDistanceStage.DistanceProjection, 35900)]
    private RuleQueryEvaluation MouCaoHongDistance(CharacterState source, CharacterState target, RuleQueryEvaluation original)
    {
        if (source.Seat == target.Seat) return original;
        var grants = ProgramEventHistory<MouCaoHongDistanceIssuedEvent>().Where(d => d.Turn == _turnNumber && d.TurnOwner == _currentSeat &&
            (d.Source.OwnerSeat == source.Seat && d.Target == target.Seat || d.Source.OwnerSeat == target.Seat && d.Target == source.Seat) &&
            !ProgramEventHistory<TurnEndedEvent>().Any(t => t.TurnNumber == d.Turn && t.ActorSeat == d.TurnOwner) && _contentRegistry.GetSkill(d.Source.SkillId).Program?.GameplayHash == d.GameplayHash &&
            ProgramEventHistory<MouCaoHongStartedEvent>().Count(s => s.FrameId == d.FrameId && s.Source == d.Source && s.GameplayHash == d.GameplayHash && s.Operation == SkillProgramEffectOp.ResolveMouCaoHongYingjia && s.ActionId == d.ActionId && s.Turn == d.Turn && s.TurnOwner == d.TurnOwner) == 1).ToArray();
        return grants.Length == 0 ? original : RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue), [new RuleQueryBaseTerm("distance:before-mou-cao-hong", ConvertRuleValue(original))],
            grants.Select(g => (RuleQueryContribution)new FiniteRuleQueryContribution($"mou-cao-hong:{g.FrameId}:{g.Target}", SkillRuleOperation.Set, 1)).ToArray());
    }
    [ProgramCapability]
    private sealed class MouCaoHongFacts(GameEngine e) : IMouCaoHongFacts
    {
        public int Turn => e._turnNumber; public int TurnOwner => e._turnProgression.OwnerSeat; public int PhaseId => e._cardUseDebitPhaseInstanceId; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int s) => e.IsValidPlayerSeat(s) && e._players[s].IsAlive; public bool Human(int s) => e._players[s].IsHuman; public string Name(int s) => e._players[s].Name; public int Keep(int owner, int card) => GetKeepValue(e.GetAdvancedCard(card), e._players[owner]);
        public SkillProgram? Program(string skill) => e._contentRegistry.Skills.GetValueOrDefault(skill)?.Program; public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public bool ActualPlay(int owner) => owner == e._currentSeat && e._phase == TurnPhase.Play && e.CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner && e.IsRecordedActualDiscardRecoveryPhase(p);
        public bool AllDistanceOne(int owner) => e._players.Where(p => p.IsAlive && p.Seat != owner).All(p => e.GetCombatDistance(owner, p.Seat) == 1 && e.GetCombatDistance(p.Seat, owner) == 1);
        public IReadOnlyList<int> InRange(int owner) => Array.AsReadOnly(e._players.Where(p => p.IsAlive && p.Seat != owner && e.IsWithinAttackRange(owner, p.Seat)).Select(p => p.Seat).Order().ToArray());
        public int OutsideCount(int owner) => e._players.Count(p => p.IsAlive && p.Seat != owner && !e.IsWithinAttackRange(owner, p.Seat));
        public IReadOnlyList<int> ExchangeTargets(int owner) => Array.AsReadOnly(e._players.Where(p => p.IsAlive && p.Seat != owner && e.GetHand(p).Count > 0 &&
            !History<MouCaoHongExchangeIssuedEvent>().Any(x => x.Owner == owner && x.Turn == Turn && x.Target == p.Seat)).Select(p => p.Seat).Order().ToArray());
        public IReadOnlyList<MouCaoHongCard> Hand(int owner) => Array.AsReadOnly(e.GetHand(e._players[owner]).Select(c => new MouCaoHongCard(c.Id, c.Kind, CardLocation.Hand(owner), c.DisplayName)).ToArray());
        public IReadOnlyList<MouCaoHongCard> Cards(int actor, int holder, bool discard) => !Alive(actor) || !Alive(holder) ? [] : Array.AsReadOnly(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.SelectMany(z =>
            e._cardZones.CardsAt(new(z, holder)).Where(c => !e.IsForeignEquipmentDiscardPrevented(actor, c, new(z, holder), discard ? OwnedCardMoveIntent.Discard : actor == holder ? OwnedCardMoveIntent.Transfer : OwnedCardMoveIntent.Obtain))
            .Select(c => new MouCaoHongCard(c.Id, c.Kind, new(z, holder), c.DisplayName))).ToArray());
        public MouCaoHongXianjuIssuedEvent? Debt(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => e.CurrentMouCaoHongDebt(c, w);
        public MouCaoHongUse? Use(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
        {
            if (w.Window != SkillProgramTriggerWindow.CardUseCompleted || w.OwnerSeat != c.OwnerSeat || w.CardUse is not { } info || e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramCardTriggerWindowFrame p ||
                p.Action.ActionId != info.CardActionId || p.Action.Type != CardActionType.Use || p.Action.ActorSeat != c.OwnerSeat || p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count) return null;
            var current = p.Candidates[p.CandidateIndex]; if (current.OwnerSeat != c.OwnerSeat || current.SkillId != c.SkillId || current.TriggerId != c.BindingId || current.SkillInstanceId != c.SkillInstanceId || current.GameplayHash != c.GameplayHash ||
                e.LifecycleCardUse(info.ParentCardUseFrameId) is not { } use || use.Action?.ActionId != p.Action.ActionId || use.SourceSeat != c.OwnerSeat) return null;
            return new(p.Id, use.Id, p.Action.ActionId, Array.AsReadOnly(use.TargetSeats.Where(s => e.IsValidPlayerSeat(s) && s != c.OwnerSeat).Distinct().Order().ToArray()));
        }
        public bool Parent(ProgramSkillFrame f)
        {
            if (f.TriggerId is null) return f.SkillId == MouCaoHongComposition.Xianju && f.ActivationId == "obtain-in-range" && f.WindowContext is null && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 &&
                History<ProgramSkillStartedEvent>().Count(p => p.FrameId == f.Id && p.SkillId == f.SkillId && p.OwnerSeat == f.OwnerSeat && p.ActivationId == f.ActivationId) == 1;
            if (f.TriggerId != f.ActivationId || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat) return false;
            var n = e._resolutionStack.FindIndex(x => x.Id == f.Id); if (n < 1 || e._resolutionStack[n - 1].Id != w.ParentFrameId) return false;
            if (f.SkillId == MouCaoHongComposition.Yingjia && f.TriggerId == "used-other-targets") return Use(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), w) is not null;
            return f.SkillId == MouCaoHongComposition.Xianju && f.TriggerId == "issued-play-end" && CurrentMouCaoHongDebt(e, f, w);
        }
        private static bool CurrentMouCaoHongDebt(GameEngine e, ProgramSkillFrame f, ProgramSkillWindowContext w) => e.CurrentMouCaoHongDebt(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0, w.OccurrenceIndex), w) is not null;
    }
    [ProgramCapability]
    private sealed class MouCaoHongNative(GameEngine e) : IMouCaoHongNative
    {
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true); public void Publish() => e.AdvanceRulesAndPublishState();
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public MouCaoHongCard RandomHand(ProgramSkillFrame f, IReadOnlyList<MouCaoHongCard> cards)
        {
            if (!e.MouCaoHong.Valid(f) || f.MouCaoHong is not { Stage: MouCaoHongStage.ObtainCard } r || cards.Count == 0 || cards.Any(c => c.From != CardLocation.Hand(r.Target))) throw new InvalidOperationException("Xianju requires the original actor's blind current opposing-Hand choice.");
            var before = e._random.State; var card = cards[e._random.Next(cards.Count)]; e.AdvanceEventRulesAndQueueFact(new MouCaoHongHandChosenEvent(f.Id, r.Target, card.Id, before, e._random.State, cards.Count)); return card;
        }
        public void Move(ProgramSkillFrame f)
        {
            if (!e.MouCaoHong.Valid(f) || f.MouCaoHong is not { } r || r.Cards.Count == 0 || r.Stage is not (MouCaoHongStage.ExchangeTakeChildren or MouCaoHongStage.ReturnChildren or MouCaoHongStage.ObtainChildren or MouCaoHongStage.DiscardChildren)) throw new InvalidOperationException("Mou Cao Hong movement requires its exact current paid segment.");
            var discard = r.Stage == MouCaoHongStage.DiscardChildren; var give = r.Stage == MouCaoHongStage.ReturnChildren; var holder = give || discard ? f.OwnerSeat : r.Target;
            if (r.Cards.Any(c => !new MouCaoHongFacts(e).Cards(f.OwnerSeat, holder, discard).Contains(c)) || r.Stage == MouCaoHongStage.ExchangeTakeChildren && r.Cards.Any(c => c.From != CardLocation.Hand(r.Target))) throw new InvalidOperationException("Mou Cao Hong cannot replace its current native HE material with a different entity or region.");
            var destination = discard ? CardLocation.DiscardPile : CardLocation.Hand(give ? r.Target : f.OwnerSeat); var ids = r.Cards.Select(c => c.Id).ToArray();
            void Paid(long batch, IReadOnlyList<CardMovementRecord> moves)
            {
                var parent = e.GetActiveProgramFrame(f.Id); var paid = parent.MouCaoHong!;
                if (!e.MouCaoHong.Valid(parent) || moves.Count != r.Cards.Count || moves.Any(m => !r.Cards.Any(c => c.Id == m.CardId && c.Kind == m.CardKind && c.From == m.From) || m.Sequence <= r.Before || m.To != destination && m.To != CardLocation.OutsideGame)) throw new InvalidOperationException("Mou Cao Hong lost its original primary physical movement receipt.");
                e.ReplaceRuntimeFrame(f.Id, parent with { MouCaoHong = paid with { After = moves.Max(m => m.Sequence), Actual = moves.Count(m => discard || m.To == destination) } });
                foreach (var m in moves) e.AdvanceEventRulesAndQueueFact(new MouCaoHongMovementPaidEvent(f.Id, r.Cursor, m.CardId, m.From, m.To, m.Sequence));
                if (give) e.RecordNativeGivenCards(f.Id, f.OwnerSeat, r.Target, ids, r.Before);
            }
            if (discard) e.MoveProgramLogicalDiscard(f, f.OwnerSeat, ids, new(MouCaoHongRuntime.DiscardReason), Paid);
            else e.MoveProgramCardsFromMultipleSources(ids, destination, new(give ? MouCaoHongRuntime.GiveReason : MouCaoHongRuntime.TakeReason), Paid);
        }
    }
}
