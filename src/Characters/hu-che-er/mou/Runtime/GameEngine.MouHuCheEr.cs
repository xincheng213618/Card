namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouHuCheErRuntime MouHuCheErRuntime => GetRuntimeModule<MouHuCheErRuntime>();
    private bool HasMouHuCheEr => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouHuCheErKongwu);
    private bool ValidMouHuCheEr(ProgramSkillFrame f) => MouHuCheErRuntime.Valid(f);
    private void ContinueMouHuCheEr(long id) => MouHuCheErRuntime.Continue(id);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 35800, SkillProgramEffectOp.ResolveMouHuCheErKongwu)]
    private bool CanStartMouHuCheEr(CharacterState owner) => MouHuCheErRuntime.CanStart(owner.Seat);
    [ProgramCapability] private sealed class MouHuCheErFacts(GameEngine e) : IMouHuCheErFacts
    {
        public int Turn => e._turnNumber; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman; public bool HasHp(int seat) => e._players[seat].HasHpValue;
        public bool Friendly(int owner, int other) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0,0,0,1,0,0,false,false)) > 0;
        public int Hp(int seat) => e._players[seat].Hp; public int MaxHp(int seat) => e._players[seat].MaxHp; public int HandCount(int seat) => e.GetHand(e._players[seat]).Count;
        public string Name(int seat) => e._players[seat].Name;
        public string SkillName(string id) => e._contentRegistry.GetSkill(id).Name; public string SkillDescription(string id) => e._contentRegistry.GetSkill(id).Description;
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!;
        public bool Owns(ProgramSkillFrame f) => e.HasRuntimeSkillInstance(e._players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
        public ActualDiscardRecoveryPhaseKey? Phase() => e.CurrentActualDiscardRecoveryPhase(); public bool Recorded(ActualDiscardRecoveryPhaseKey p) => e.IsRecordedActualDiscardRecoveryPhase(p);
        public IReadOnlyList<int> Targets(int owner) => Array.AsReadOnly(e._players.Where(t => t.IsAlive && t.Seat != owner && e.GetHand(t).Count > 0).Select(t => t.Seat).ToArray());
        public IReadOnlyList<MouHuCheErHand> Hand(int target) => Array.AsReadOnly(e.GetHand(e._players[target]).Select((c,i) => new MouHuCheErHand(c.Id, i, c.Kind, e.GetProgramEffectiveSuit(e._players[target], c))).ToArray());
        public bool InHand(int target, int id) => e._cardZones.GetLocation(id) == CardLocation.Hand(target);
        public SkillProgramCardCategory Category(CardKind kind) => GetProgramCardCategory(kind);
        public IReadOnlyList<int> SameSuitDiscards(int target, Suit suit) => Array.AsReadOnly(e.GetHand(e._players[target]).Where(c => e.GetProgramEffectiveSuit(e._players[target], c) == suit && !e.IsSelfHandCategoryDiscardForbidden(target, c, CardLocation.Hand(target), OwnedCardMoveIntent.Discard)).Select(c => c.Id).ToArray());
        public int CategoryCount(int target, SkillProgramCardCategory category) => e.GetHand(e._players[target]).Concat(e.GetEquipment(target)).Count(c => GetProgramCardCategory(c.Kind) == category);
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public MouHuCheErDamageEvent? Damage(IGameEvent payload)
        {
            if (payload is not DamageAppliedEvent { Amount: > 0 } applied || e.CurrentDamageAttempt is not CardAttackHandle a || a.IsChainPropagation || e.LifecycleCardUse(a.ResolutionId) is not { MouHuCheErSlash: { } o } use || !e.IsIssuedMouHuCheErUse(use) || applied.TargetSeat != o.Target || a.TargetSeat != o.Target || !a.DamageWasApplied) return null;
            return new(o.FrameId, use.Id, applied.TargetSeat, applied.Amount);
        }
    }
    [ProgramCapability] private sealed class MouHuCheErNative(GameEngine e) : IMouHuCheErNative
    {
        public void PublishState() => e.AdvanceRulesAndPublishState(); public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) ||
            e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public bool CanSlash(ProgramSkillFrame f) => e.CanMouHuCheErSlash(f); public void Slash(ProgramSkillFrame f) => e.IssueMouHuCheErSlash(f);
        public void Reveal(ProgramSkillFrame f)
        {
            if (!e.ValidMouHuCheEr(f) || f.MouHuCheEr is not { Stage: MouHuCheErStage.DiscardChildren } r || e._cardZones.GetLocation(r.Shown) != CardLocation.Hand(r.Target)) throw new InvalidOperationException("Kongwu reveal requires its actual selected Hand entity.");
            e.AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, [e.ToSnapshot(e.GetAdvancedCard(r.Shown))]));
            e.AdvanceEventRulesAndQueueFact(new MouHuCheErShownEvent(f.Id, r.Target, r.Shown, r.Suit, r.Category));
        }
        public void Discard(ProgramSkillFrame f)
        {
            if (!e.ValidMouHuCheEr(f) || f.MouHuCheEr is not { Stage: MouHuCheErStage.DiscardChildren } r || !r.Discards.SequenceEqual(new MouHuCheErFacts(e).SameSuitDiscards(r.Target, r.Suit))) throw new InvalidOperationException("Kongwu discards require its actual payable whole same-suit Hand group.");
            e.MoveProgramLogicalDiscard(f, r.Target, r.Discards, new(MouHuCheErRuntime.MouHuCheErDiscardReason), (_, moves) => {
                var parent = e.GetActiveProgramFrame(f.Id); var receipt = parent.MouHuCheEr!;
                if (!moves.Select(m => m.CardId).SequenceEqual(r.Discards) || moves.Any(m => m.From != CardLocation.Hand(r.Target) || m.Sequence <= receipt.Before || m.To != CardLocation.DiscardPile && m.To != CardLocation.OutsideGame)) throw new InvalidOperationException("Kongwu lost its real same-suit discard entities.");
                e.ReplaceRuntimeFrame(f.Id, parent with { MouHuCheEr = receipt with { After = moves.Max(m => m.Sequence) } });
                foreach (var m in moves) e.AdvanceEventRulesAndQueueFact(new MouHuCheErDiscardedEvent(f.Id, r.Target, m.CardId, m.Sequence)); });
        }
    }
}
