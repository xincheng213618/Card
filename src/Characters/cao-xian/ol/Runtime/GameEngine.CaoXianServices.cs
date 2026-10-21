namespace CardGame.Core;
public sealed partial class GameEngine
{
    private CaoXianRuntime CaoXianRules => GetRuntimeModule<CaoXianRuntime>();
    private bool HasCaoXian => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.StoreCaoXianLingxi);
    [ProgramCapability] private sealed class CaoXianFacts(GameEngine e) : ICaoXianFacts
    {
        public int Turn => e._turnNumber; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive; public bool Human(int seat) => e._players[seat].IsHuman; public string Name(int seat) => e._players[seat].Name; public int MaxHp(int seat) => e._players[seat].MaxHp; public int HandCount(int seat) => e.GetHand(e._players[seat]).Count;
        public int Keep(int owner, int card) => GetKeepValue(e.GetAdvancedCard(card), e._players[owner]);
        public bool Friendly(int owner, int other) => owner == other || (e._aiBrains.TryGetValue(owner, out var brain) ? brain.ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0, 0, 0, 1, 0, 0, false, false)) > 0 : e._players[owner].TeamRevealed && e._players[other].TeamRevealed && e._players[owner].TeamId is { } team && e._players[other].TeamId == team);
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!; public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>(); public IReadOnlyList<int> Living() => Array.AsReadOnly(e._players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray());
        public IReadOnlyList<PublicPersistentPileSource> Piles(int owner, string consumerInstance) => Array.AsReadOnly(e.ReferencedPublicPileSources(owner, CaoXianRuntime.Lingxi, consumerInstance).ToArray());
        public IReadOnlyList<CaoXianCard> PileCards(PublicPersistentPileSource pile) => Array.AsReadOnly(e.PublicPileSources(pile.OwnerSeat).Any(p => p == pile) ? e.PublicPileCards(pile).Select(c => new CaoXianCard(c.Id, c.Kind, c.Suit, pile.Location, c.DisplayName)).ToArray() : []);
        public IReadOnlyList<CaoXianCard> Owned(int owner, bool handOnly, bool discard) => !Alive(owner) ? [] : Array.AsReadOnly((handOnly ? new[] { CardZoneKind.Hand } : new[] { CardZoneKind.Hand, CardZoneKind.Equipment }).SelectMany(z => e._cardZones.CardsAt(new(z, owner)).Where(c => !e.IsSelfHandCategoryDiscardForbidden(owner, c, new(z, owner), discard ? OwnedCardMoveIntent.Discard : OwnedCardMoveIntent.Transfer) && (!discard || !e.IsForeignEquipmentDiscardPrevented(owner, c, new(z, owner), OwnedCardMoveIntent.Discard))).Select(c => new CaoXianCard(c.Id, c.Kind, c.Suit, new(z, owner), c.DisplayName))).ToArray());
        public CaoXianCompletedUse? Completed(ProgramSkillWindowContext w, int owner)
        {
            if (w.Window != SkillProgramTriggerWindow.CardUseCompleted || w.OwnerSeat != owner || w.CardUse is not { } info || e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramCardTriggerWindowFrame p || p.Action.ActorSeat != owner || info.CardActionId != p.Action.ActionId || p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count || p.Candidates[p.CandidateIndex].OwnerSeat != owner) return null;
            return new(info.ParentCardUseFrameId, p.Action.ActionId);
        }
        public PublicPersistentPileSource? RemovedPile(ProgramSkillWindowContext w, string instance)
        {
            if (w.Window != SkillProgramTriggerWindow.CardsMoved || w.MovementBatch is not { } batch || e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not CardsMovedTriggerWindowFrame p || p.Id != batch.Id || !p.Batch.Movements.SequenceEqual(batch.Movements) || !e._publicPersistentPiles.TryGetValue((w.OwnerSeat, CaoXianRuntime.Lingxi, instance), out var pile)) return null;
            return batch.Movements.Any(m => m.From == pile.Location && m.To != m.From && e._cardMovements.Contains(m)) ? pile : null;
        }
        public bool Parent(ProgramSkillFrame f)
        {
            if (f.TriggerId != f.ActivationId || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat) return false;
            var at = e._resolutionStack.FindIndex(x => x.Id == f.Id); if (at < 1 || e._resolutionStack[at - 1].Id != w.ParentFrameId) return false;
            if (e._resolutionStack[at - 1] is ProgramCardTriggerWindowFrame card)
            {
                if (card.CandidateIndex < 0 || card.CandidateIndex >= card.Candidates.Count || w.CardUse?.CardActionId != card.Action.ActionId || Completed(w, f.OwnerSeat) is null) return false;
                var c = card.Candidates[card.CandidateIndex]; return c.OwnerSeat == f.OwnerSeat && c.SkillId == f.SkillId && c.TriggerId == f.TriggerId && c.SkillInstanceId == f.SkillInstanceId && c.GameplayHash == f.GameplayHash;
            }
            var candidate = e._resolutionStack[at - 1] switch {
                PlayPhaseStartingBoundaryFrame p when w.Window == SkillProgramTriggerWindow.PlayPhaseStarting && p.OwnerSeat == f.OwnerSeat && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count => p.Items[p.ItemIndex].Candidate,
                ProgramLifecycleTriggerWindowFrame p when p.Window == w.Window && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => p.Candidates[p.CandidateIndex],
                CardsMovedTriggerWindowFrame p when w.Window == SkillProgramTriggerWindow.CardsMoved && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => p.Candidates[p.CandidateIndex], _ => null };
            return candidate is not null && MountObserverCandidateMatches(f, candidate);
        }
    }
    [ProgramCapability] private sealed class CaoXianNative(GameEngine e) : ICaoXianNative
    {
        public PublicPersistentPileSource EnsurePile(ProgramSkillFrame f) => f.SkillId == CaoXianRuntime.Lingxi && f.CaoXian?.Operation == SkillProgramEffectOp.StoreCaoXianLingxi ? e.EnsurePublicPileSource(f, int.MaxValue) : throw new InvalidOperationException("Only the actual Lingxi depositor may create her source-specific wings.");
        public void Publish() => e.AdvanceRulesAndPublishState(); public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void LoseHp(ProgramSkillFrame f) => new ProgramSkillHost(e).LoseHp(f.Id, f.SkillId, f.CaoXian!.Target, 1);
        public void Move(ProgramSkillFrame f)
        {
            if (!e.CaoXianRules.Valid(f) || f.CaoXian is not { Cards.Count: > 0 } r) throw new InvalidOperationException("Cao Xian requires this exact frozen paid native segment.");
            var remove = r.Stage == CaoXianStage.RemoveChildren; var discard = r.Stage == CaoXianStage.AdjustChildren || r.Option == 2;
            var payer = r.Operation == SkillProgramEffectOp.StoreCaoXianLingxi || r.Stage == CaoXianStage.AdjustChildren ? f.OwnerSeat : r.Target;
            var current = remove ? new CaoXianFacts(e).PileCards(r.Pile!) : new CaoXianFacts(e).Owned(payer, r.Stage == CaoXianStage.AdjustChildren, discard);
            if (r.Cards.Any(c => !current.Contains(c))) throw new InvalidOperationException("Cao Xian cannot move stale wings or another holder's unavailable HE entity.");
            var destination = remove ? CardLocation.OutsideGame : discard ? CardLocation.DiscardPile : r.Pile!.Location;
            void Paid(long batch, IReadOnlyList<CardMovementRecord> moves)
            {
                if (moves.Any(m => !e._cardMovements.Contains(m) || m.To != destination && m.To != CardLocation.OutsideGame)) throw new InvalidOperationException("Cao Xian's native movement changed its admitted destination.");
                e.CaoXianRules.MovesPaid(f.Id, moves);
            }
            if (discard) e.MoveProgramLogicalDiscard(f, payer, r.Cards.Select(c => c.Id).ToArray(), new("skill-program.ol:lingxi-or-zhifou.discard"), Paid);
            else e.MoveProgramCardsFromMultipleSources(r.Cards.Select(c => c.Id).ToArray(), destination, new(remove ? "skill-program.ol:zhifou.remove-wing" : "skill-program.ol:lingxi.store-wing"), Paid, disclosure: CardMovementDisclosure.FaceUp);
        }
    }
}
