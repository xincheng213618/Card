namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouGuanPingRuntime MouGuanPing => GetRuntimeModule<MouGuanPingRuntime>();
    [ProgramCapability]
    private sealed class MouGuanPingFacts(GameEngine e) : IMouGuanPingFacts
    {
        public int Turn => e._turnNumber; public int TurnOwner => e._currentSeat; public int PhaseId => e._cardUseDebitPhaseInstanceId;
        public bool Finished => e._winner != Winner.None;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman;
        public SkillProgram Program() => e._contentRegistry.GetSkill(MouGuanPingComposition.Skill).Program!;
        public int PhaseUsage(int owner) => e._programPhaseUses.GetValueOrDefault((owner, MouGuanPingComposition.Skill, MouGuanPingComposition.Activation));
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public bool WholeColor(int owner, IReadOnlyList<int> ids)
        {
            if (ids.Count == 0 || ids.Distinct().Count() != ids.Count || !Alive(owner)) return false;
            var hand = e.GetHand(e._players[owner]);
            var cards = hand.Where(c => ids.Contains(c.Id)).ToArray();
            if (cards.Length != ids.Count || SuitColor(e.EffectiveSuit(e._players[owner], cards[0])) is not { } red) return false;
            return hand.Where(c => SuitColor(e.EffectiveSuit(e._players[owner], c)) == red).Select(c => c.Id).Order().SequenceEqual(ids.Order());
        }
        public IReadOnlyList<ProgramMultiCardViewAsSelection> Groups(int owner)
        {
            if (!Alive(owner) || !e.HasRuntimeSkill(e._players[owner], MouGuanPingComposition.Skill)) return [];
            var actor = e._players[owner]; var source = new CardConversionSource(MouGuanPingComposition.Skill, MouGuanPingComposition.Binding, owner, e.GetRuntimeSkillInstanceId(actor, MouGuanPingComposition.Skill));
            return Array.AsReadOnly(new[] { true, false }.Select(red => e.GetHand(actor).Where(c => SuitColor(e.EffectiveSuit(actor, c)) == red).Select(c => c.Id).Order().ToArray())
                .Where(ids => ids.Length > 0).Select(ids => e.FindProgramMultiCardViewAsSelection(actor, ids, CardKind.Slash, false, source)).Where(s => s is not null).Select(s => ProgramSelectionProjection.Freeze(s)!).ToArray());
        }
        public IReadOnlyList<int> Targets(int owner, IReadOnlyList<Card> cards)
        {
            var actor = e._players[owner]; var selection = new ProgramMultiCardViewAsSelection(cards, new(MouGuanPingComposition.Skill, MouGuanPingComposition.Binding, owner, e.GetRuntimeSkillInstanceId(actor, MouGuanPingComposition.Skill)), CardKind.Slash);
            return Array.AsReadOnly(e._players.Where(t => e.CanUseVirtualSlashTarget(actor, t, physicalSuit: e.ProgramMultiCardSuit(actor, selection), effectiveColor: e.ProgramMultiCardColor(actor, selection),
                physicalCardIds: cards.Select(c => c.Id).ToArray(), ignoreDistance: true, conversionSource: selection.Source, countsTowardLimit: false)).Select(t => t.Seat).ToArray());
        }
        public CardUseFrame? Use(long id) => e.LifecycleCardUse(id);
        public CardUseFrame? Use(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
        {
            if (c.SkillId != MouGuanPingComposition.Skill || c.BindingId != MouGuanPingComposition.Trigger || w.Window != SkillProgramTriggerWindow.CardUseCommitted || w.OwnerSeat != c.OwnerSeat || w.CardUse is not { } info ||
                e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramCardTriggerWindowFrame window || window.Action.ActionId != info.CardActionId ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || window.Candidates[window.CandidateIndex] is not { } current || current.OwnerSeat != c.OwnerSeat ||
                current.SkillId != c.SkillId || current.TriggerId != c.BindingId || current.SkillInstanceId != c.SkillInstanceId || current.GameplayHash != c.GameplayHash) return null;
            return e._resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == info.CardActionId && u.MouGuanPing is { } r && r.Source.OwnerSeat == c.OwnerSeat && r.Source.SkillInstanceId == c.SkillInstanceId && r.GameplayHash == c.GameplayHash);
        }
    }
    [ProgramCapability]
    private sealed class MouGuanPingNative(GameEngine e) : IMouGuanPingNative
    {
        public void PublishState() => e.AdvanceRulesAndPublishState();
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void SetMask(ProgramSkillFrame f, int mask)
        {
            if (f.MouGuanPing is not { } r || r.Mask != mask || e.LifecycleCardUse(r.Use.UseId) is not { MouGuanPing: { } receipt } use || receipt.ActionId != r.Use.ActionId ||
                receipt != r.Use with { OptionMask = receipt.OptionMask }) throw new InvalidOperationException("Wuwei may only modify its own original issued Slash.");
            e.UpdateLifecycleCardUse(use.Id, u => u with { MouGuanPing = receipt with { OptionMask = mask } });
        }
        public void Suppress(ProgramSkillFrame f, int target)
        {
            if (!e.MouGuanPing.Valid(f) || f.MouGuanPing is not { Stage: MouGuanPingStage.Children } r || !r.Targets.Contains(target)) throw new InvalidOperationException("Wuwei suppression requires an actual original Slash target.");
            e.IssueCurrentTurnNonLockedSkillSuppression(f, target);
        }
    }
    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 35000)]
    private bool MouGuanPingSingleCardAlias(ProgramConfiguredViewAsQuery q) => q.Instance.SkillId == MouGuanPingComposition.Skill;
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 35000)]
    private bool MouGuanPingWholeColorBan(ProgramViewAsLegalityQuery q) => q.Source.SkillId == MouGuanPingComposition.Skill &&
        (q.Source.BindingId != MouGuanPingComposition.Binding || q.Kind != CardKind.Slash || q.ForResponse || q.DyingUse || !new MouGuanPingFacts(this).WholeColor(q.Owner.Seat, q.CardIds));
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 35000)]
    private bool MouGuanPingActualActivationQuota(ProgramSlashQuotaQuery q) => q.Kind == CardKind.Slash && q.PhysicalCardIds is { Count: > 0 } ids &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { SkillId: MouGuanPingComposition.Skill, ActivationId: MouGuanPingComposition.Activation, TriggerId: null, InstructionIndex: 1 } f &&
        f.OwnerSeat == q.Actor.Seat && f.SelectedTargetSeats.Contains(q.Target.Seat) && f.SelectedCardIds.Order().SequenceEqual(ids.Order()) &&
        f.GameplayHash == _contentRegistry.GetSkill(f.SkillId).Program!.GameplayHash && HasRuntimeSkillInstance(q.Actor, f.SkillId, f.SkillInstanceId) && new MouGuanPingFacts(this).WholeColor(f.OwnerSeat, ids);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 35000)]
    private void IssueMouGuanPingUse(CardUseDeclaredEvent declared)
    {
        if (LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use } a } use ||
            a.ConversionChain.SingleOrDefault(s => s.SkillId == MouGuanPingComposition.Skill) is not { BindingId: MouGuanPingComposition.Binding } source) return;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { SkillId: MouGuanPingComposition.Skill, ActivationId: MouGuanPingComposition.Activation, TriggerId: null, InstructionIndex: 1 } f ||
            f.OwnerSeat != source.OwnerSeat || source.SkillInstanceId != f.SkillInstanceId || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.EffectiveKind != CardKind.Slash ||
            !a.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(f.SelectedCardIds.Order()) || a.PhysicalCards.Any(c => c.From != CardLocation.Hand(f.OwnerSeat)) ||
            !new MouGuanPingFacts(this).WholeColor(f.OwnerSeat, f.SelectedCardIds) || use.MouGuanPing is not null)
            throw new InvalidOperationException("Wuwei requires its native activation and all currently payable same-color Hand entities.");
        var types = a.PhysicalCards.Select(c => CardUseCategoryCatalog.Get(c.CardKind)).Select(c => c is CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick ? CardUseCategories.InstantTrick : c).Distinct().Count();
        var extra = ProgramEventHistory<MouGuanPingUseIssuedEvent>().Any(e => e.Receipt.Source.OwnerSeat == f.OwnerSeat && e.Receipt.ActualTurn == _turnNumber && e.Receipt.TurnOwner == _currentSeat && e.Receipt.PhaseId == _cardUseDebitPhaseInstanceId);
        var receipt = new MouGuanPingUseReceipt(use.Id, a.ActionId, f.Id, source, f.GameplayHash, _turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId, types, extra);
        UpdateLifecycleCardUse(use.Id, u => u with { MouGuanPing = receipt, UnlimitedUse = true }); AdvanceEventRulesAndQueueFact(new MouGuanPingUseIssuedEvent(receipt));
    }
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 35000)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 35000)]
    private bool MouGuanPingIssuedDistance(ProgramSlashDistanceQuery q) => q.Conversion is { SkillId: MouGuanPingComposition.Skill, BindingId: MouGuanPingComposition.Binding } s && s.OwnerSeat == q.Actor.Seat &&
        (q.ExistingUseFrameId is { } id ? LifecycleCardUse(id)?.MouGuanPing?.Source == s : q.PhysicalCardIds is { } ids && new MouGuanPingFacts(this).WholeColor(q.Actor.Seat, ids));
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 35000)]
    private int MouGuanPingDamage(IDamageAttempt attack) => !attack.IsChainPropagation && attack is CardAttackHandle &&
        LifecycleCardUse(attack.ResolutionId) is { Action: { } a, MouGuanPing: { OptionMask: 7 } r } u && r.UseId == u.Id && r.ActionId == a.ActionId &&
        r.Source.OwnerSeat == a.ActorSeat && ProgramEventHistory<MouGuanPingCompletedEvent>().Count(e => e.UseId == u.Id && e.Mask == 7 && e.Count == 3) == 1 ? 1 : 0;
    [ProgramPipelineHook(ProgramPipelinePhase.AdmitTurnSuppression, 35000)]
    private bool MouGuanPingPaidSuppression(ProgramSkillFrame f, int seat) => f.MouGuanPing is { Stage: MouGuanPingStage.Children } r && r.Targets.Contains(seat) && MouGuanPing.Valid(f);
}
