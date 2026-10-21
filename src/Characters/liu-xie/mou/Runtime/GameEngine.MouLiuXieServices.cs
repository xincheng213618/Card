namespace CardGame.Core;

public sealed partial class GameEngine
{
    private MouLiuXieRuntime MouLiuXie => GetRuntimeModule<MouLiuXieRuntime>();
    [ProgramCapability]
    private sealed class MouLiuXieFacts(GameEngine e) : IMouLiuXieFacts
    {
        public int Turn => e._turnNumber;
        public int CurrentSeat => e._currentSeat;
        public int PlayerCount => e._playerCount;
        public int TableStepSeat(int from, int offset) => e.TableStepSeat(from, offset);
        public long MovementSequence => e._movementSequence;
        public bool Finished => e._winner != Winner.None;
        public ActualDiscardRecoveryPhaseKey? Phase => e.CurrentActualDiscardRecoveryPhase();
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman;
        public bool Qun(int seat) => e.GetEffectiveFactionId(e._players[seat]) == "qun";
        public bool LordQualified(int seat) => e.HasRuntimeSkill(e._players[seat], MouLiuXieComposition.Tiancheng) &&
            e.HasSkillRoleQualification(e._players[seat], MouLiuXieComposition.Tiancheng, null, Role.Lord);
        public bool Friendly(int owner, int target) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), target, new(0, 0, 0, 1, 0, 0, false, false)) > 0;
        public string Name(int seat) => e._players[seat].Name;
        public string CardLabel(int id) => e.GetAdvancedCard(id).DisplayName;
        public int HandCount(int seat) => e.GetHand(e._players[seat]).Count;
        public IReadOnlyList<int> Discardable(int seat) => Array.AsReadOnly(e.GetHand(e._players[seat]).Where(c =>
            !e.IsSelfHandCategoryDiscardForbidden(seat, c, CardLocation.Hand(seat), OwnedCardMoveIntent.Discard)).Select(c => c.Id).ToArray());
        public int KeepValue(int seat, int id) => GetKeepValue(e.GetAdvancedCard(id), e._players[seat]);
        public SkillProgram? Program(string skill) => e._contentRegistry.Skills.GetValueOrDefault(skill)?.Program;
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public bool ExactStart(ProgramSkillFrame f) => f.SkillId == MouLiuXieComposition.Zhanban && f.ActivationId == "adjust-all-hands" &&
            f.TriggerId is null && f.WindowContext is null && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && f.InstructionIndex == 1 &&
            e.GetPausedPrivateOfferEffect(f)?.Op == SkillProgramEffectOp.ResolveMouLiuXieZhanban && Program(f.SkillId)?.GameplayHash == f.GameplayHash &&
            e.HasRuntimeSkillInstance(e._players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) &&
            Phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == f.OwnerSeat && e.IsRecordedActualDiscardRecoveryPhase(p) &&
            !History<MouLiuXieStartedEvent>().Any(x => x.Source.OwnerSeat == f.OwnerSeat && x.Phase == p);
        public bool ChenshengParent(ProgramSkillFrame f)
        {
            if (f.SkillId != MouLiuXieComposition.Chensheng || f.TriggerId != "other-actual-turn-end" || f.ActivationId != f.TriggerId ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded } w || w.OwnerSeat != f.OwnerSeat ||
                Program(f.SkillId)?.GameplayHash != f.GameplayHash || e._currentSeat == f.OwnerSeat) return false;
            var n = e._resolutionStack.FindIndex(x => x.Id == f.Id);
            return n > 0 && e._resolutionStack[n - 1] is DeferredTurnEndFrame parent && parent.Id == w.ParentFrameId &&
                parent.OwnerSeat == e._currentSeat && parent.TurnNumber == e._turnNumber && w.SourceSeat == parent.OwnerSeat && w.TargetSeat == parent.OwnerSeat &&
                e.IsActualAfterTurnEndedParent(parent) && MountObserverCandidateMatches(f, e.AfterTurnEndedCandidate(parent));
        }
    }
    [ProgramCapability]
    private sealed class MouLiuXieNative(GameEngine e) : IMouLiuXieNative
    {
        public void PublishState() => e.AdvanceRulesAndPublishState();
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) ||
            e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Damage(ProgramSkillFrame f, int target) => e.BeginProgramSkillDamage(f, target, 1);
        public void Discard(ProgramSkillFrame f)
        {
            if (!e.MouLiuXie.Valid(f) || f.MouLiuXie is not { Stage: MouLiuXieStage.DiscardChildren } r ||
                r.Paying.Count == 0 || r.Paying.Any(id => !new MouLiuXieFacts(e).Discardable(r.Payer).Contains(id)))
                throw new InvalidOperationException("Zhanban must discard this payer's exact legal Hand entities.");
            e.MoveProgramLogicalDiscard(f, r.Payer, r.Paying, new(MouLiuXieRuntime.DiscardReason), (_, records) =>
            {
                var parent = e._resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(x => x.Id == f.Id);
                if (parent?.MouLiuXie is not { Stage: MouLiuXieStage.DiscardChildren } paid || !e.MouLiuXie.Valid(parent) ||
                    paid.Payer != r.Payer || !paid.Paying.SequenceEqual(r.Paying) || records.Count != r.Paying.Count ||
                    records.Any(m => !e._cardMovements.Contains(m) || !r.Paying.Contains(m.CardId) || m.From != CardLocation.Hand(r.Payer) ||
                        m.Sequence <= paid.Before || m.Reason.Value != MouLiuXieRuntime.DiscardReason ||
                        m.To != CardLocation.DiscardPile && m.To != CardLocation.OutsideGame))
                    throw new InvalidOperationException("Zhanban's native discard lost its original parent or primary movement receipt.");
                e.ReplaceRuntimeFrame(f.Id, parent = parent with { MouLiuXie = paid with { After = records.Max(m => m.Sequence) } });
                foreach (var moved in records)
                    e.AdvanceEventRulesAndQueueFact(new MouLiuXieDiscardPaidEvent(f.Id, paid.Payer, moved.CardId, moved.Sequence, paid.IsAdjustment));
            });
        }
    }
}
