namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouCaoShuangAnniversaryRuntime MouCaoShuangAnniversary => GetRuntimeModule<MouCaoShuangAnniversaryRuntime>();
    [ProgramCapability]
    private sealed class MouCaoShuangAnniversaryFacts(GameEngine e) : IMouCaoShuangAnniversaryFacts
    {
        public int Turn => e._turnNumber; public int CurrentSeat => e._currentSeat; public int PhaseId => e._cardUseDebitPhaseInstanceId;
        public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman; public string Name(int seat) => e._players[seat].Name;
        public int Keep(int seat, int card) => GetKeepValue(e.GetAdvancedCard(card), e._players[seat]);
        public bool Friendly(int owner, int other) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0,0,0,1,0,0,false,false)) > 0;
        public IReadOnlyList<int> Living() => Array.AsReadOnly(e._players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray());
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!;
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public bool ActualPlay(int owner) => e._currentSeat == owner && e._phase == TurnPhase.Play &&
            e.CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner && e.IsRecordedActualDiscardRecoveryPhase(p);
        public bool Parent(ProgramSkillFrame f)
        {
            if (f.TriggerId != f.ActivationId || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat) return false;
            var i = e._resolutionStack.FindIndex(x => x.Id == f.Id); if (i < 1 || e._resolutionStack[i - 1].Id != w.ParentFrameId) return false;
            var parent = e._resolutionStack[i - 1];
            if (parent is ProgramCardTriggerWindowFrame card)
            {
                if (card.CandidateIndex < 0 || card.CandidateIndex >= card.Candidates.Count || w.CardUse?.CardActionId != card.Action.ActionId) return false;
                var c = card.Candidates[card.CandidateIndex]; return c.OwnerSeat == f.OwnerSeat && c.SkillId == f.SkillId && c.TriggerId == f.TriggerId && c.SkillInstanceId == f.SkillInstanceId && c.GameplayHash == f.GameplayHash;
            }
            var candidate = parent switch {
                ProgramLifecycleTriggerWindowFrame p when p.Window == w.Window && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => p.Candidates[p.CandidateIndex],
                TurnEndingBoundaryFrame p when p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count => p.Items[p.ItemIndex].Candidate,
                _ => null };
            return candidate is not null && MountObserverCandidateMatches(f, candidate);
        }
        public MouCaoShuangAnniversaryUse? Use(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
        {
            if (w.CardUse is not { } info || e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramCardTriggerWindowFrame p ||
                p.Action.ActionId != info.CardActionId || p.Action.ActorSeat != c.OwnerSeat || p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count) return null;
            var candidate = p.Candidates[p.CandidateIndex];
            if (candidate.OwnerSeat != c.OwnerSeat || candidate.SkillId != c.SkillId || candidate.TriggerId != c.BindingId || candidate.SkillInstanceId != c.SkillInstanceId || candidate.GameplayHash != c.GameplayHash) return null;
            var use = e.LifecycleCardUse(info.ParentCardUseFrameId);
            if (w.Window == SkillProgramTriggerWindow.CardUseCommitted) return new(p.Id, p.Action, use?.Id ?? info.ParentCardUseFrameId, -1, p.Action.EffectiveIsRed);
            if (w.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || use is null || use.TargetSeats is not [var target] || target == c.OwnerSeat || !Alive(target)) return null;
            return new(p.Id, p.Action, use.Id, target, p.Action.EffectiveIsRed ?? (p.Action.EffectiveSuit is { } suit && suit != Suit.None ? suit is Suit.Heart or Suit.Diamond : null));
        }
        public IReadOnlyList<MouCaoShuangAnniversaryCard> Payable(int payer, bool recast) => !Alive(payer) ? [] : Array.AsReadOnly((recast ? new[] { CardZoneKind.Hand } : new[] { CardZoneKind.Hand, CardZoneKind.Equipment })
            .SelectMany(z => e._cardZones.CardsAt(new(z, payer)).Where(c => recast || !e.IsForeignEquipmentDiscardPrevented(payer, c, new(z, payer), OwnedCardMoveIntent.Discard))
                .Select(c => new MouCaoShuangAnniversaryCard(c.Id, c.Kind, new(z, payer), c.DisplayName))).ToArray());
        public bool Hurt(int source, int victim) => e.ProgramEventHistory<DamageAppliedEvent>().Any(d => !d.SourceLess && d.Amount > 0 && d.SourceSeat == source && d.TargetSeat == victim);
    }
    [ProgramCapability]
    private sealed class MouCaoShuangAnniversaryNative(GameEngine e) : IMouCaoShuangAnniversaryNative
    {
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public void PublishState() => e.AdvanceRulesAndPublishState();
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Move(ProgramSkillFrame f)
        {
            if (!e.MouCaoShuangAnniversary.Valid(f) || f.MouCaoShuangAnniversary is not { Stage: MouCaoShuangAnniversaryStage.MoveChildren, Cards.Count: > 0 } r || r.Cards.Any(c => !new MouCaoShuangAnniversaryFacts(e).Payable(r.Payer, r.Option == 3).Contains(c))) throw new InvalidOperationException("Jianzhuan requires the actual chooser's frozen available entities.");
            var ids = r.Cards.Select(c => c.Id).ToArray();
            void Paid(long batch, IReadOnlyList<CardMovementRecord> records)
            {
                var parent = e.GetActiveProgramFrame(f.Id); var paid = parent.MouCaoShuangAnniversary!;
                if (records.Count != r.Cards.Count || !records.Zip(r.Cards).All(pair => pair.First.CardId == pair.Second.Id && pair.First.CardKind == pair.Second.Kind && pair.First.From == pair.Second.From && pair.First.Sequence > r.Before &&
                    (pair.First.To == CardLocation.DiscardPile || pair.First.To == CardLocation.OutsideGame))) throw new InvalidOperationException("Jianzhuan lost its actual discard or recast entities.");
                e.ReplaceRuntimeFrame(f.Id, parent with { MouCaoShuangAnniversary = paid with { After = records.Max(m => m.Sequence) } });
                foreach (var m in records) e.AdvanceEventRulesAndQueueFact(new MouCaoShuangAnniversaryMaterialEvent(f.Id, r.Repeat, r.Payer, m.CardId, m.From, m.Sequence, r.Option == 3));
            }
            if (r.Option == 3)
            {
                e.ReplaceRuntimeTop(f = e.BeginFrameNativeRecastInvocation(f, r.Payer, ids, r.Repeat));
                e.MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, CardMoveReasons.RecastDiscard, Paid, disclosure: CardMovementDisclosure.FaceUp);
            }
            else e.MoveProgramLogicalDiscard(f, r.Payer, ids, new(MouCaoShuangAnniversaryRuntime.DiscardReason), Paid);
        }
        public void CompleteRecast(ProgramSkillFrame f, int actual)
        {
            var r = f.MouCaoShuangAnniversary!;
            if (!e.MouCaoShuangAnniversary.Valid(f) || r.Option != 3 || r.Stage != MouCaoShuangAnniversaryStage.DrawChildren || r.Draw?.ActualDrawIds.Count != actual) throw new InvalidOperationException("Jianzhuan recast requires its actual typed draw result.");
            for (var i = 0; i < r.Cards.Count; i++) e.AdvanceEventRulesAndQueueFact(new CardRecastEvent(r.Payer, r.Cards[i].Id, r.Cards[i].Kind, i < actual ? 1 : 0));
            e.CompleteFrameNativeRecastInvocation(f, actual);
        }
        public void Delete(ProgramSkillFrame f)
        {
            if (!e.MouCaoShuangAnniversary.Valid(f) || f.MouCaoShuangAnniversary is not { Operation: SkillProgramEffectOp.DeleteMouCaoShuangJianzhuanOption } r) throw new InvalidOperationException("Jianzhuan deletion requires its actual completed Play.");
            var items = MouCaoShuangAnniversaryRuntime.Items(r.Remaining); var before = e._random.State; var item = items[e._random.Next(items.Length)];
            e.AdvanceEventRulesAndQueueFact(new MouCaoShuangAnniversaryDeletedEvent(f.Id, f.OwnerSeat, r.Turn, r.PhaseId, item, r.Remaining, r.Remaining & ~(1 << (item - 1)), before, e._random.State));
        }
        public void Maximum(ProgramSkillFrame f) => e.ChangeProgramMaximumHp(f, 2);
        public void Recover(ProgramSkillFrame f) => new ProgramSkillHost(e).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 2, null, null);
        public void Grant(ProgramSkillFrame f)
        {
            if (!e.MouCaoShuangAnniversary.Valid(f) || f.MouCaoShuangAnniversary is not { Stage: MouCaoShuangAnniversaryStage.Grant }) throw new InvalidOperationException("Fanshi grant requires its completed native repeats and recovery.");
            foreach (var p in e._players) e._observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
            var owner = e._players[f.OwnerSeat]; foreach (var grant in owner.SkillGrants.Grants.Where(g => g.SkillId == MouCaoShuangAnniversaryRuntime.Jianzhuan).ToArray()) owner.SkillGrants.RemoveGrant(grant.GrantId);
            e.AcquireRuntimeSkills(owner, f.SkillId, [MouCaoShuangAnniversaryRuntime.Fudou]);
            e.AdvanceEventRulesAndQueueFact(new MouCaoShuangAnniversaryGrantedEvent(f.Id, f.OwnerSeat));
        }
        public void LoseHp(ProgramSkillFrame f, int target) => new ProgramSkillHost(e).LoseHp(f.Id, f.SkillId, target, 1);
    }
}
