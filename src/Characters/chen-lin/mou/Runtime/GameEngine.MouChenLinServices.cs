namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouChenLinRuntime MouChenLinRules => GetRuntimeModule<MouChenLinRuntime>();
    private bool HasMouChenLin => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouChenLinZhuanwen);
    private bool ValidMouChenLin(ProgramSkillFrame f) => MouChenLinRules.Valid(f);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 37000, SkillProgramEffectOp.ResolveMouChenLinYaozuo)] private bool CanStartMouChenLinInvitation(CharacterState owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner.Seat && IsRecordedActualDiscardRecoveryPhase(p);
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 37000)] private bool MouChenLinDamageTargets(ProgramTargetCardWindowQuery q) => HasMouChenLin && MouChenLinRuntime.DamageCard(q.Kind) && IsOrdinaryTrick(q.Kind);
    [ProgramCapability] private sealed class MouChenLinFacts(GameEngine e) : IMouChenLinFacts
    {
        public int Turn => e._turnNumber; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int s) => e.IsValidPlayerSeat(s) && e._players[s].IsAlive; public bool Human(int s) => e._players[s].IsHuman; public string Name(int s) => e._players[s].Name; public int HandCount(int s) => e.GetHand(e._players[s]).Count;
        public bool Friendly(int owner, int other) => owner == other || (e._aiBrains.TryGetValue(owner, out var brain) ? brain.ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0, 0, 0, 1, 0, 0, false, false)) > 0 : e._players[owner].TeamRevealed && e._players[other].TeamRevealed && e._players[owner].TeamId is { } team && e._players[other].TeamId == team);
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!;
        public string SkillName(string id) => e._contentRegistry.GetSkill(id).Name; public string SkillDescription(string id) => e._contentRegistry.GetSkill(id).Description;
        public ActualDiscardRecoveryPhaseKey? Phase() => e.CurrentActualDiscardRecoveryPhase(); public bool Recorded(ActualDiscardRecoveryPhaseKey phase) => e.IsRecordedActualDiscardRecoveryPhase(phase);
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public IReadOnlyList<int> Living() => e._players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray();
        public IReadOnlyList<MouChenLinCard> He(int actor) => !Alive(actor) ? [] : Array.AsReadOnly(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.SelectMany(z => e._cardZones.CardsAt(new(z, actor)).Where(c => !e.IsSelfHandCategoryDiscardForbidden(actor, c, new(z, actor), OwnedCardMoveIntent.Transfer)).Select(c => new MouChenLinCard(c.Id, c.Kind, new(z, actor), c.DisplayName))).ToArray());
        public IReadOnlyList<MouChenLinCard> Current(IEnumerable<int> ids) => Array.AsReadOnly(ids.Select(id => { var c = e.GetAdvancedCard(id); return new MouChenLinCard(id, c.Kind, e._cardZones.GetLocation(id), c.DisplayName); }).ToArray());
        public IReadOnlyList<MouChenLinCard> Revealed(ProgramSkillFrame f) => Current(f.CardSetBindings.Single(b => b.Name == MouChenLinRuntime.RevealBind).CardIds);
        public MouChenLinUsePlan? Plan(ProgramSkillFrame f, int id, int target) => e.MouChenLinPlan(f, id, target);
        public bool Parent(ProgramSkillFrame f)
        {
            if (f.SkillId == MouChenLinRuntime.Yaozuo) return f.TriggerId is null && f.ActivationId == "invite-authors" && f.WindowContext is null && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && History<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.ActivationId == f.ActivationId && e.OwnerSeat == f.OwnerSeat) == 1;
            if (f.SkillId != MouChenLinRuntime.Zhuanwen || f.TriggerId != "ending-top-written-cards" || f.ActivationId != f.TriggerId || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } w || w.OwnerSeat != f.OwnerSeat) return false;
            var n = e._resolutionStack.FindIndex(x => x.Id == f.Id);
            return n >= 1 && e._resolutionStack[n - 1] is TurnEndingBoundaryFrame p && p.Id == w.ParentFrameId && p.OwnerSeat == f.OwnerSeat && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && MountObserverCandidateMatches(f, p.Items[p.ItemIndex].Candidate);
        }
    }
    [ProgramCapability] private sealed class MouChenLinNative(GameEngine e) : IMouChenLinNative
    {
        public void Publish() => e.AdvanceRulesAndPublishState(); public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Reveal(ProgramSkillFrame f)
        {
            if (!e.ValidMouChenLin(f) || f.MouChenLin is not { Stage: MouChenLinStage.Revealing } r || f.CardSetBindings.Any(b => b.Name == MouChenLinRuntime.RevealBind)) throw new InvalidOperationException("Zhuanwen needs its original unspent native top reveal.");
            e.RevealProgramTopCards(f.Id, f.OwnerSeat, r.Requested, null, MouChenLinRuntime.RevealBind, SkillProgramCardSetVisibility.Public);
        }
        public void Use(ProgramSkillFrame f) => e.IssueMouChenLinUse(f);
        public void Give(ProgramSkillFrame f)
        {
            if (!e.MouChenLinRules.ValidInvitation(f) || f.MouChenLinInvitation is not { Stage: MouChenLinInvitationStage.MovementChildren, Closed: false, PendingCard: { } card } r || !r.Eligible.Contains(r.PendingGiver) || r.Answered.Contains(r.PendingGiver) || !new MouChenLinFacts(e).He(r.PendingGiver).Contains(card) || !e._players[f.OwnerSeat].IsAlive) throw new InvalidOperationException("Yaozuo needs this actual participant's current original HE entity.");
            e.MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(f.OwnerSeat), new("skill-program.mou:yaozuo.give"), (_, moves) => {
                if (moves is not [var m] || m.CardId != card.Id || m.From != card.From || m.Sequence <= r.Before || m.To != CardLocation.Hand(f.OwnerSeat) && m.To != CardLocation.OutsideGame) throw new InvalidOperationException("Yaozuo lost its original native Give payment movement.");
                var parent = e.GetActiveProgramFrame(f.Id); e.ReplaceRuntimeFrame(f.Id, parent with { MouChenLinInvitation = parent.MouChenLinInvitation! with { After = m.Sequence } });
                if (m.To == CardLocation.Hand(f.OwnerSeat)) e.RecordMouChenLinGift(e.GetActiveProgramFrame(f.Id), r.PendingGiver, card.Id, r.Before, moves);
                e.RecordNativeGivenCards(f.Id, r.PendingGiver, f.OwnerSeat, [card.Id], r.Before);
            });
        }
        public void CloseInvitation(ProgramSkillFrame f) => e.IssueMouChenLinNonHandDonorTickets(f);
        public void Move(ProgramSkillFrame f)
        {
            if (!e.ValidMouChenLin(f) || f.MouChenLin is not { Stage: MouChenLinStage.GiveChildren or MouChenLinStage.ReturningChildren } r) throw new InvalidOperationException("Zhuanwen needs its own real public-card transfer segment.");
            var give = r.Stage == MouChenLinStage.GiveChildren;
            var ids = r.Cards.Where(c => e._cardZones.GetLocation(c.Id) == CardLocation.Processing && (!give || !MouChenLinRuntime.DamageCard(c.Kind))).Select(c => c.Id).ToArray();
            if (ids.Length == 0) return;
            if (give && !e._players[r.Target].IsAlive) return;
            var destination = give ? CardLocation.Hand(r.Target) : CardLocation.DrawPile;
            e.MoveProgramCardsFromMultipleSources(ids, destination, new(give ? MouChenLinRuntime.GiveReason : MouChenLinRuntime.ReturnReason), (_, moves) => {
                if (moves.Any(m => m.To != destination && m.To != CardLocation.OutsideGame)) throw new InvalidOperationException("Zhuanwen changed the destination of its retained physical entities.");
                if (!give) e._cardZones.PlaceDrawPileCardsAtTop(moves.Where(m => m.To == CardLocation.DrawPile).Select(m => m.CardId).ToArray());
                e.MouChenLinRules.MovesPaid(f.Id, moves);
                if (give && r.Target != f.OwnerSeat) e.RecordNativeGivenCards(f.Id, f.OwnerSeat, r.Target, ids, r.Before);
            }, disclosure: CardMovementDisclosure.FaceUp);
        }
    }
}
