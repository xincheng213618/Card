namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouSimaShiRuntime MouSimaShiRules => GetRuntimeModule<MouSimaShiRuntime>();
    private bool HasMouSimaShi => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeMouSimaShiSanshi);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 36400, SkillProgramEffectOp.ResolveMouSimaShiChenlve)] private bool CanStartMouSimaShiChenlve(CharacterState owner) => MouSimaShiRules.CanActivate(owner.Seat);
    [ProgramCapability] private sealed class MouSimaShiFacts(GameEngine e) : IMouSimaShiFacts
    {
        public int Turn => e._turnNumber; public long Sequence => e._movementSequence;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman; public string Name(int seat) => e._players[seat].Name;
        public bool Friendly(int owner, int other) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0, 0, 0, 1, 0, 0, false, false)) > 0;
        public int HandCount(int seat) => e.GetHand(e._players[seat]).Count;
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!;
        public ActualDiscardRecoveryPhaseKey? Phase() => e.CurrentActualDiscardRecoveryPhase();
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public IReadOnlyList<MouSimaShiDeckCard> Deck() => Array.AsReadOnly(e._cardZones.CardsAt(CardLocation.DrawPile).Select(c => new MouSimaShiDeckCard(c.Id, c.Rank)).ToArray());
        public IReadOnlyList<MouSimaShiCard> Entities(IEnumerable<int> ids) => Array.AsReadOnly(ids.Distinct().Select(id => { var p = e._cardZones.GetLocation(id); return new MouSimaShiCard(id, e.GetAdvancedCard(id).Kind, p); }).ToArray());
        public bool Quarantined(MouSimaShiRemovedEvent removed) => e._cardZones.GetLocation(removed.CardId) == CardLocation.OutsideGame && e._cardMovements.LastOrDefault(m => m.CardId == removed.CardId) is { } actual && actual.Sequence == removed.Sequence && actual.To == CardLocation.OutsideGame && e.ProgramEventHistory<MouSimaShiMovedEvent>().Count(m => m.DebtId == removed.DebtId && m.CardId == removed.CardId && m.Sequence == actual.Sequence && m.Operation == SkillProgramEffectOp.RemoveMouSimaShiChenlve && m.To == actual.To) == 1;
        public MouSimaShiClaimIssuedEvent? Due(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => e.MouSimaShiDue(c, w);
        public MouSimaShiUse? Use(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
        {
            if (w.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || w.CardUse is not { } info || e._resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is not ProgramCardTriggerWindowFrame p || p.Action.Type != CardActionType.Use || p.Action.ActionId != info.CardActionId || p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count || e.LifecycleCardUse(info.ParentCardUseFrameId) is not { Action: { } a } use || a.ActionId != p.Action.ActionId) return null;
            var actual = p.Candidates[p.CandidateIndex]; if (actual.OwnerSeat != c.OwnerSeat || actual.SkillId != c.SkillId || actual.TriggerId != c.BindingId || actual.SkillInstanceId != c.SkillInstanceId || actual.GameplayHash != c.GameplayHash) return null;
            return new(use.Id, a.ActionId, a.ActorSeat, Array.AsReadOnly(use.TargetSeats.ToArray()));
        }
        public bool Parent(ProgramSkillFrame f)
        {
            if (f.TriggerId is null) return f.SkillId == MouSimaShiRuntime.Chenlve && f.ActivationId == "claim-dead-soldiers" && f.WindowContext is null && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && History<ProgramSkillStartedEvent>().Count(s => s.FrameId == f.Id && s.SkillId == f.SkillId && s.OwnerSeat == f.OwnerSeat && s.ActivationId == f.ActivationId) == 1;
            if (f.TriggerId != f.ActivationId || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat) return false;
            var n = e._resolutionStack.FindIndex(x => x.Id == f.Id); if (n < 1 || e._resolutionStack[n - 1].Id != w.ParentFrameId) return false;
            var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0, w.OccurrenceIndex);
            if (f.SkillId == MouSimaShiRuntime.Chenlve) return Due(candidate, w) is not null;
            if (f.SkillId == MouSimaShiRuntime.Zhenrao) return Use(candidate, w) is not null;
            return e._resolutionStack[n - 1] switch {
                ProgramLifecycleTriggerWindowFrame p => p.Window == w.Window && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count && MountObserverCandidateMatches(f, p.Candidates[p.CandidateIndex]),
                TurnEndingBoundaryFrame p => p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && MountObserverCandidateMatches(f, p.Items[p.ItemIndex].Candidate), _ => false };
        }
    }
    [ProgramCapability] private sealed class MouSimaShiNative(GameEngine e) : IMouSimaShiNative
    {
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true); public void Publish() => e.AdvanceRulesAndPublishState();
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public (int Id, ulong Before, ulong After) Random(IReadOnlyList<MouSimaShiDeckCard> cards) { var before = e._random.State; var selected = cards[e._random.Next(cards.Count)]; return (selected.Id, before, e._random.State); }
        public void Move(ProgramSkillFrame f, MouSimaShiCard card, CardLocation destination)
        {
            if (!e.MouSimaShiRules.Valid(f) || f.MouSimaShi is not { Stage: MouSimaShiStage.MovementChildren } r || r.Index < 1 || r.Cards[r.Index - 1] != card || e._cardZones.GetLocation(card.Id) != card.From) throw new InvalidOperationException("Mou Sima Shi needs his exact current physical movement.");
            e.MoveProgramCardsFromMultipleSources([card.Id], destination, new("skill-program.mou:sima-shi." + r.Operation), (_, moves) => e.MouSimaShiRules.MovesPaid(f.Id, moves), disclosure: card.From.Zone is CardZoneKind.DrawPile or CardZoneKind.Hand || destination == CardLocation.DrawPile || destination == CardLocation.OutsideGame ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        }
        public void Damage(ProgramSkillFrame f)
        {
            if (!e.MouSimaShiRules.Valid(f) || f.MouSimaShi is not { Stage: MouSimaShiStage.Damage, Operation: SkillProgramEffectOp.ResolveMouSimaShiZhenrao } r || !e._players[r.Target].IsAlive) throw new InvalidOperationException("Zhenrao damage requires the accepted actual-use target.");
            e.BeginProgramSkillDamage(f, r.Target, 1, explicitSourceSeat: f.OwnerSeat);
        }
        public void Shuffle(ProgramSkillFrame f)
        {
            if (!e.MouSimaShiRules.Valid(f) || f.MouSimaShi is not { Stage: MouSimaShiStage.Shuffle, Operation: SkillProgramEffectOp.ReturnMouSimaShiChenlve } || e.ProgramEventHistory<MouSimaShiShuffledEvent>().Any(s => s.FrameId == f.Id)) throw new InvalidOperationException("Chenlve shuffles only after its actual death return movements.");
            var before = e._random.State; e._cardZones.Shuffle(CardLocation.DrawPile, e._random); e.AdvanceEventRulesAndQueueFact(new MouSimaShiShuffledEvent(f.Id, e._cardZones.Count(CardLocation.DrawPile), before, e._random.State));
        }
    }
}
