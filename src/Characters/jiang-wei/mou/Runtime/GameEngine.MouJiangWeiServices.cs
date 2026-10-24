namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouJiangWeiAnniversaryRuntime MouJiangWeiAnniversary => GetRuntimeModule<MouJiangWeiAnniversaryRuntime>();
    private bool IsMouJiangWeiFuzhanExitContext(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (c.SkillId != MouJiangWeiAnniversaryRuntime.Fuzhan || GetProgramTrigger(c).Effects is not [{ Op: SkillProgramEffectOp.ResolveMouJiangWeiFuzhan }] ||
            _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame p || p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count ||
            p.Candidates[p.CandidateIndex] != c || !IsYanPuDyingExitEdge(_resolutionStack.FindIndex(f => f.Id == p.Id))) return false;
        return new MouJiangWeiAnniversaryFacts(this).CanTrigger(c, w, SkillProgramEffectOp.ResolveMouJiangWeiFuzhan);
    }
    [ProgramCapability]
    private sealed class MouJiangWeiAnniversaryFacts(GameEngine e) : IMouJiangWeiAnniversaryFacts
    {
        public int Turn => e._turnNumber; public long MovementSequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman;
        public int Hp(int seat) => e._players[seat].Hp; public int MaxHp(int seat) => e._players[seat].MaxHp;
        public string Name(int seat) => e._players[seat].Name;
        public bool Friendly(int owner, int other) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0, 0, 0, 1, 0, 0, false, false)) > 0;
        public IReadOnlyList<int> Living() => Array.AsReadOnly(e._players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray());
        public string Instance(int owner) => e.GetRuntimeSkillInstanceId(e._players[owner], MouJiangWeiAnniversaryRuntime.Juemou);
        public bool State(int owner, string instance, string state) => e.GetProgramBooleanState(owner, MouJiangWeiAnniversaryRuntime.Juemou, instance, state);
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!;
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public bool Parent(ProgramSkillFrame f)
        {
            if (f.TriggerId != f.ActivationId || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat) return false;
            var i = e._resolutionStack.FindIndex(x => x.Id == f.Id); if (i < 1 || e._resolutionStack[i - 1].Id != w.ParentFrameId) return false;
            var parent = e._resolutionStack[i - 1];
            if (parent is DeferredTurnEndFrame ended) return w.Window == SkillProgramTriggerWindow.AfterTurnEnded && e.IsActualAfterTurnEndedParent(ended) && ended.OwnerSeat == f.OwnerSeat && MountObserverCandidateMatches(f, e.AfterTurnEndedCandidate(ended));
            if (parent is ProgramCardTriggerWindowFrame card)
            {
                if (card.CandidateIndex < 0 || card.CandidateIndex >= card.Candidates.Count || w.CardUse?.CardActionId != card.Action.ActionId) return false;
                var c = card.Candidates[card.CandidateIndex];
                return c.OwnerSeat == f.OwnerSeat && c.SkillId == f.SkillId && c.TriggerId == f.TriggerId && c.SkillInstanceId == f.SkillInstanceId && c.GameplayHash == f.GameplayHash;
            }
            var candidate = parent switch {
                ProgramLifecycleTriggerWindowFrame p when p.Window == w.Window && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => p.Candidates[p.CandidateIndex],
                TurnEndingBoundaryFrame p when p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count => p.Items[p.ItemIndex].Candidate,
                _ => null };
            return candidate is not null && MountObserverCandidateMatches(f, candidate);
        }
        public long Action(ProgramSkillWindowContext w) => w.Window == SkillProgramTriggerWindow.CardUseCommitted && w.CardUse is { } info &&
            e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is ProgramCardTriggerWindowFrame p && p.Action.ActionId == info.CardActionId && p.Action.ActorSeat == w.OwnerSeat &&
            CardUseCategoryCatalog.Get(p.Action.EffectiveKind) is CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick ? p.Action.ActionId : 0;
        public (long Cause, long Dying)? CausalDying(int owner, ProgramSkillWindowContext w)
        {
            if (w.Window != SkillProgramTriggerWindow.DyingEntering || e.ActiveDying is not { } dying || dying.VictimSeat != owner ||
                e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.DyingEntering, Continuation: ProgramLifecycleContinuation.ResumeDyingEntry } entry ||
                entry.ResumeDyingFrameId != dying.Id || entry.OwnerSeat != owner ||
                e._resolutionStack.SingleOrDefault(f => f.Id == dying.ParentFrameId) is not DamageFrame damage || damage.TargetSeat != owner ||
                e._resolutionStack.SingleOrDefault(f => f.Id == damage.ParentFrameId) is not ProgramSkillFrame { MouJiangWeiAnniversary: { Operation: SkillProgramEffectOp.ResolveMouJiangWeiJuemou, Stage: MouJiangWeiAnniversaryStage.Damage } r } root ||
                root.OwnerSeat != owner || root.AttackAttempt is not { } attempt || attempt.TargetSeat != owner || attempt.SourceSeat != r.DamageSource || r.Victim != owner ||
                !e.MouJiangWeiAnniversary.Valid(root) || !e.ProgramEventHistory<PlayerDyingEvent>().Any(p => p.ResolutionId == dying.Id && p.VictimSeat == owner) ||
                e.ProgramEventHistory<MouJiangWeiAnniversaryDamageIssuedEvent>().Count(p => p.FrameId == root.Id && p.Victim == owner && p.Source == attempt.SourceSeat) != 1) return null;
            return (root.Id, dying.Id);
        }
        public bool CanTrigger(ProgramTriggerCandidate c, ProgramSkillWindowContext w, SkillProgramEffectOp operation)
        {
            if (w.OwnerSeat != c.OwnerSeat || !Alive(c.OwnerSeat)) return false;
            if (operation == SkillProgramEffectOp.InitializeMouJiangWeiJuemou) return c.SkillId == MouJiangWeiAnniversaryRuntime.Juemou && w.Window == SkillProgramTriggerWindow.GameStarting;
            if (operation == SkillProgramEffectOp.RestoreMouJiangWeiJuemou) return c.SkillId == MouJiangWeiAnniversaryRuntime.Juemou && CausalDying(c.OwnerSeat, w) is not null;
            if (operation == SkillProgramEffectOp.ResolveMouJiangWeiJuemou)
            {
                if (c.SkillId != MouJiangWeiAnniversaryRuntime.Juemou || State(c.OwnerSeat, Instance(c.OwnerSeat), "yin") && Living().Count < 2) return false;
                return Action(w) > 0 || State(c.OwnerSeat, Instance(c.OwnerSeat), "modified") && w.Window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.AfterTurnEnded && w.TargetSeat == c.OwnerSeat;
            }
            if (operation != SkillProgramEffectOp.ResolveMouJiangWeiFuzhan || c.SkillId != MouJiangWeiAnniversaryRuntime.Fuzhan || w.Window != SkillProgramTriggerWindow.DyingExited ||
                e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.DyingExited, Continuation: ProgramLifecycleContinuation.ResumeYanPuDyingExit, YanPuDyingExit: { } ret } exited ||
                w.SourceSeat != ret.VictimSeat || w.TargetSeat != ret.VictimSeat || !Alive(ret.VictimSeat) || Hp(ret.VictimSeat) <= 0) return false;
            return e.ProgramEventHistory<DyingResolvedEvent>().Count(p => p.ResolutionId == ret.DyingFrameId && p.VictimSeat == ret.VictimSeat && p.Survived) == 1;
        }
        public IReadOnlyList<MouJiangWeiAnniversaryCard> Discardable(int chooser, int holder) => !Alive(chooser) || !Alive(holder) ? [] : Array.AsReadOnly(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.SelectMany(z =>
            e._cardZones.CardsAt(new(z, holder)).Where(c => !e.IsForeignEquipmentDiscardPrevented(chooser, c, new(z, holder), OwnedCardMoveIntent.Discard)).Select(c => new MouJiangWeiAnniversaryCard(c.Id, new(z, holder), c.DisplayName))).ToArray());
    }
    [ProgramCapability]
    private sealed class MouJiangWeiAnniversaryNative(GameEngine e) : IMouJiangWeiAnniversaryNative
    {
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public void PublishState() => e.AdvanceRulesAndPublishState();
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void State(ProgramSkillFrame f, string state, bool value)
        {
            if (!e.MouJiangWeiAnniversary.Valid(f) || f.MouJiangWeiAnniversary is not { } r || state is not ("yin" or "modified")) throw new InvalidOperationException("Juemou state requires its original accepted branch or Fuzhan.");
            e.SetProgramBooleanState(f, MouJiangWeiAnniversaryRuntime.Juemou, r.JuemouInstance, state, value);
        }
        public MouJiangWeiAnniversaryCard RandomHand(ProgramSkillFrame f, IReadOnlyList<MouJiangWeiAnniversaryCard> cards)
        {
            if (!e.MouJiangWeiAnniversary.Valid(f) || f.MouJiangWeiAnniversary is not { Stage: MouJiangWeiAnniversaryStage.Discard } r || cards.Count == 0 || cards.Any(c => c.From != CardLocation.Hand(r.DamageSource))) throw new InvalidOperationException("Juemou random Hand choice requires the actual payable hidden Hand group.");
            var before = e._random.State; var selected = cards[e._random.Next(cards.Count)];
            e.AdvanceEventRulesAndQueueFact(new MouJiangWeiAnniversaryHandChosenEvent(f.Id, selected.Id, before, e._random.State, cards.Count)); return selected;
        }
        public void Discard(ProgramSkillFrame f)
        {
            if (!e.MouJiangWeiAnniversary.Valid(f) || f.MouJiangWeiAnniversary is not { Stage: MouJiangWeiAnniversaryStage.DiscardChildren, Card: { } card } r || !new MouJiangWeiAnniversaryFacts(e).Discardable(r.Victim, r.DamageSource).Contains(card)) throw new InvalidOperationException("Juemou discard requires the chosen actual other-role HE entity.");
            e.MoveProgramLogicalDiscard(f, r.Victim, [card.Id], new(MouJiangWeiAnniversaryRuntime.CostReason), (_, records) => {
                var parent = e.GetActiveProgramFrame(f.Id); var paid = parent.MouJiangWeiAnniversary!;
                if (!e.MouJiangWeiAnniversary.Valid(parent) || records is not [var m] || m.CardId != card.Id || m.From != card.From || m.Sequence <= paid.Before || m.To != CardLocation.DiscardPile && m.To != CardLocation.OutsideGame) throw new InvalidOperationException("Juemou lost its primary real foreign discard receipt.");
                e.ReplaceRuntimeFrame(f.Id, parent with { MouJiangWeiAnniversary = paid with { After = m.Sequence } });
                e.AdvanceEventRulesAndQueueFact(new MouJiangWeiAnniversaryDiscardedEvent(f.Id, paid.Victim, paid.DamageSource, card.Id, card.From, m.Sequence)); });
        }
        public void Damage(ProgramSkillFrame f)
        {
            if (!e.MouJiangWeiAnniversary.Valid(f) || f.MouJiangWeiAnniversary is not { Stage: MouJiangWeiAnniversaryStage.Damage } r) throw new InvalidOperationException("Juemou damage requires its accepted real Yin/Yang branch.");
            e.BeginProgramSkillDamage(f, r.Victim, 1, explicitSourceSeat: r.DamageSource);
        }
        public void Recover(ProgramSkillFrame f, int amount)
        {
            if (!e.MouJiangWeiAnniversary.Valid(f) || f.MouJiangWeiAnniversary is not { Stage: MouJiangWeiAnniversaryStage.RecoveryChildren } r || r.Requested != amount) throw new InvalidOperationException("Juemou/Fuzhan recovery requires its real native recovery cursor.");
            new ProgramSkillHost(e).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, amount, null, null);
        }
    }
}
