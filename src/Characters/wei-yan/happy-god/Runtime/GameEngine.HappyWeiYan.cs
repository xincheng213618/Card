using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyBoneEnter = "skill-program.happy:zigu.bone-enter", HappyBoneReplace = "skill-program.happy:zigu.bone-replace", HappyZiguGain = "skill-program.happy:zigu.damage-card";
    private static CardConversionSource HappyWeiYanSource(ProgramSkillFrame frame) => new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    [ProgramCapability] private sealed class HappyWeiYanHost(GameEngine engine) : IHappyWeiYanHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.BeginHappyWeiYan(frame, operation); }
    [ProgramCapability] private sealed class HappyWeiYanTargetQuery(GameEngine engine) : IHappyWeiYanTargetQuery
    {
        public bool Allows(int owner, int target) => engine.IsValidPlayerSeat(owner) && engine.IsValidPlayerSeat(target) && owner != target && engine._players[owner].IsAlive && engine._players[target].IsAlive && engine.HappyBoneAvailableSlots(target).Any();
    }
    private HappyWeiYanBone? HappyBoneRecord(int id) => _players.SelectMany(p => p.HappyWeiYanBones).Where(b => b.CardId == id).OrderByDescending(b => b.Sequence).FirstOrDefault();
    private HappyWeiYanBone? CurrentHappyBone(int cardId)
    {
        if (cardId <= 0 || HappyBoneRecord(cardId) is null) return null;
        foreach (var holder in _players)
        {
            var location = CardLocation.Equipment(holder.Seat);
            if (_cardZones.Contains(location, cardId)) return HappyBoneAt(cardId, location, _movementSequence);
        }
        return null;
    }
    private EquipmentSlot? HappyBoneSlot(Card card) => CurrentHappyBone(card.Id) is { } bone && bone.PrintedKind == card.Kind ? bone.Slot : null;
    private bool IsHappyWeiYanBone(int id) => CurrentHappyBone(id) is not null;
    private int? HappyBoneSourceOwner(int cardId) => IsHappyWeiYanBone(cardId) ? HappyBoneRecord(cardId)!.Source.OwnerSeat : null;
    private HappyWeiYanBone? HappyBoneAt(int cardId, CardLocation location, int sequence)
    {
        var bone = _players.SelectMany(p => p.HappyWeiYanBones).Where(b => b.CardId == cardId && b.Sequence <= sequence).OrderByDescending(b => b.Sequence).FirstOrDefault();
        if (bone is null || location.Zone != CardZoneKind.Equipment || bone.Sequence <= 0 || bone.GameplayHash != _contentRegistry.GetSkill(bone.Source.SkillId).Program!.GameplayHash ||
            ProgramEventHistory<HappyWeiYanBonePlacedEvent>().Count(e => e.FrameId == bone.FrameId && e.Source == bone.Source && e.Holder == bone.Holder && e.Slot == bone.Slot && e.After == bone.Sequence) != 1 ||
            _cardMovements.SingleOrDefault(m => m.Sequence == bone.Sequence) is not { } entry || entry.CardId != cardId || entry.CardKind != bone.PrintedKind || entry.From != CardLocation.Hand(bone.Source.OwnerSeat) || entry.To != CardLocation.Equipment(bone.Holder) || entry.Reason.Value != HappyBoneEnter) return null;
        var current = entry.To;
        foreach (var move in _cardMovements.Where(m => m.CardId == cardId && m.Sequence > entry.Sequence && m.Sequence <= sequence))
        {
            if (move.From != current || move.From.Zone != CardZoneKind.Equipment || move.To.Zone != CardZoneKind.Equipment) return null;
            current = move.To;
        }
        return current == location ? bone : null;
    }
    private bool HasExactHappyBonePlacement(Card card, int holder)
    {
        return card.Id > 0 && IsValidPlayerSeat(holder) && _cardZones.Contains(CardLocation.Equipment(holder), card.Id) && HappyBoneAt(card.Id, CardLocation.Equipment(holder), _movementSequence) is { } bone && bone.PrintedKind == card.Kind;
    }
    private IEnumerable<EquipmentSlot> HappyBoneAvailableSlots(int holder) => Enum.GetValues<EquipmentSlot>().Where(s => !_players[holder].EquipmentAreaAbolished && _players[holder].EquipmentSlotCapacity(s) > 0);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 42300)] private void ObserveHappyWeiYanFirstEquipment(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent declared || !EquipmentCatalog.IsEquipment(declared.CardKind) ||
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveHappyWeiYanZigu) || _turnNumber <= 0 || _turnProgression.TurnNumber != _turnNumber ||
            LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use ||
            ProgramEventHistory<HappyWeiYanFirstEquipmentEvent>().Any(e => e.ActualTurn == _turnNumber && e.Actor == use.SourceSeat)) return;
        AdvanceEventRulesAndQueueFact(new HappyWeiYanFirstEquipmentEvent(_turnNumber, use.SourceSeat, use.Id, action.ActionId));
    }
    private CardUseFrame? HappyZiguFirstEquipment(int owner, ProgramSkillWindowContext context) => context.CardUse is { } info && LifecycleCardUse(info.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } action } use &&
        use.SourceSeat == owner && action.ActionId == info.CardActionId && EquipmentCatalog.IsEquipment(use.CardKind) &&
        ProgramEventHistory<HappyWeiYanFirstEquipmentEvent>().Count(e => e == new HappyWeiYanFirstEquipmentEvent(_turnNumber, owner, use.Id, action.ActionId)) == 1 ? use : null;
    private bool TryHappyWeiYanEquipmentCommitted(long id, Card equipment)
    {
        if (LifecycleCardUse(id) is not { Action: { Type: CardActionType.Use } action } use ||
            !(HasProgramSkill(_players[use.SourceSeat], HappyWeiYanComposition.Zigu) && ProgramEventHistory<HappyWeiYanFirstEquipmentEvent>().Any(e => e == new HappyWeiYanFirstEquipmentEvent(_turnNumber, use.SourceSeat, id, action.ActionId))) &&
            !(HasHappyWeiYanEzhiCapability && HappyBones(use.SourceSeat).Count > 0 && action.PhysicalCards.Count > 0) && use.HappyWeiYanCancellation is null) return false;
        var continuation = new ProgramSimpleCardContinuation(equipment.Id, SimpleCardUseEffect.EquipmentPlacement);
        return TryBeginCommittedCardUse(id, ProgramCardContinuation.CommittedSimpleCard, simple: continuation) || ContinueHappyWeiYanIneffectiveSimple(id, continuation);
    }
    private static int HappyZiguFrozenCounterpart(int owner, ProgramSkillWindowContext context) => context.SourceSeat is { } source && context.TargetSeat is { } target && source != target && (source == owner || target == owner) ? source == owner ? target : source : -1;
    private int HappyZiguCounterpart(int owner, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.SourceSeat is not { } source || context.TargetSeat is not { } target || source == target ||
            source != owner && target != owner || CurrentDamageAttempt is not { IsSourceLess: false } attack || attack.SourceSeat != source || attack.TargetSeat != target) return -1;
        return source == owner ? target : source;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 42300, SkillProgramEffectOp.ResolveHappyWeiYanZigu)]
    private bool HappyZiguEligible(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Id == "first-equipment") return HappyZiguFirstEquipment(candidate.OwnerSeat, context) is not null;
        var counterpart = HappyZiguCounterpart(candidate.OwnerSeat, context);
        return counterpart >= 0 && _players[counterpart].IsAlive && GetHand(_players[candidate.OwnerSeat]).Count > 0 && HappyBoneAvailableSlots(counterpart).Any();
    }
    private SkillProgramStepOutcome BeginHappyWeiYan(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (operation != SkillProgramEffectOp.ResolveHappyWeiYanZigu) return BeginHappyWeiYanAdditional(frame, operation);
        if (frame.HappyWeiYan is not null || frame.InstructionIndex != 1 || frame.WindowContext is not { } context) throw new InvalidOperationException("Happy Wei Yan requires his original unpaid native instruction.");
        var use = frame.TriggerId == "first-equipment" ? HappyZiguFirstEquipment(frame.OwnerSeat, context) : null;
        var target = use is not null ? frame.OwnerSeat : HappyZiguCounterpart(frame.OwnerSeat, context);
        if (target < 0) throw new InvalidOperationException("Zigu lost the actual sourced damage counterpart.");
        var receipt = new HappyWeiYanReceipt(HappyWeiYanSource(frame), frame.GameplayHash, operation, context.ParentFrameId, _turnNumber)
        { Target = target, UseId = use?.Id ?? 0, ActionId = use?.Action!.ActionId ?? 0, Stage = use is null ? HappyWeiYanStage.Hand : HappyWeiYanStage.DamageTarget, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt });
        AdvanceEventRulesAndQueueFact(new HappyWeiYanStartedEvent(frame.Id, receipt.Source, receipt.GameplayHash, operation, receipt.ParentId, receipt.ActualTurn, target, receipt.UseId, receipt.ActionId));
        ContinueHappyWeiYan(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainHappyWeiYan(ProgramSkillFrame frame) => TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(frame.Id) || TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(frame.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 42300)] private bool ContinueHappyWeiYan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyWeiYan: not null } frame || frame.Id != id) return false;
        if (frame.HappyWeiYan.Operation == SkillProgramEffectOp.ResolveHappyWeiYanEzhi) return ContinueHappyEzhi(frame);
        if (frame.HappyWeiYan.Operation == SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn) return ContinueHappyBoneReturn(frame);
        while (_pendingDecision is null)
        {
            frame = GetActiveProgramFrame(id);
            RequireHappyWeiYan(frame); var receipt = frame.HappyWeiYan!;
            if (receipt.Stage is HappyWeiYanStage.Replacing or HappyWeiYanStage.Placing or HappyWeiYanStage.GainChildren)
            {
                if (DrainHappyWeiYan(frame)) return true;
                frame = GetActiveProgramFrame(id); receipt = frame.HappyWeiYan!;
                var stage = receipt.Stage == HappyWeiYanStage.Replacing ? HappyWeiYanStage.Slot : receipt.Operation == SkillProgramEffectOp.ResolveHappyWeiYanGujing ? HappyWeiYanStage.GujingSelection : HappyWeiYanStage.Finished;
                ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null, HappyWeiYan = receipt = receipt with { Stage = stage } });
                if (stage == HappyWeiYanStage.Slot) { PlaceHappyBone(frame); return true; }
            }
            if (receipt.Operation == SkillProgramEffectOp.ResolveHappyWeiYanGujing && receipt.Stage == HappyWeiYanStage.GujingDiscardChildren) return ContinueHappyGujingPayment(frame);
            if (receipt.Stage == HappyWeiYanStage.Damage)
            {
                if (frame.AttackAttempt is not null) return true;
                ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt = receipt with { Stage = HappyWeiYanStage.DamageCard } });
            }
            if (receipt.Stage == HappyWeiYanStage.DamageCard)
            {
                var pool = _cardZones.CardsAt(CardLocation.DrawPile).Where(c => IsDamageCardKind(c.Kind) || IsCompletedUndamagedDamageUse(c.Kind) || c.Kind == CardKind.Lightning).ToArray();
                if (pool.Length == 0 || !_players[frame.OwnerSeat].IsAlive) { FinishHappyWeiYan(frame); return true; }
                var card = pool[_random.Next(pool.Length)];
                PayHappyWeiYanMove(frame, HappyWeiYanStage.GainChildren, card, CardLocation.DrawPile, CardLocation.Hand(frame.OwnerSeat), HappyZiguGain); continue;
            }
            if (receipt.Stage == HappyWeiYanStage.Finished || !_players[frame.OwnerSeat].IsAlive || _winner != Winner.None) { FinishHappyWeiYan(frame); return true; }
            var choices = HappyWeiYanChoices(frame);
            if (choices.Count == 0) { FinishHappyWeiYan(frame); return true; }
            var skill = _contentRegistry.GetSkill(frame.SkillId);
            ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
            _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, skill.Name + "：请选择。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), frame.OwnerSeat)
            { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = receipt.Target, Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
            _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState(); return true;
        }
        return true;
    }
    private IReadOnlyList<PromptChoice> HappyWeiYanChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.HappyWeiYan!;
        if (receipt.Operation is SkillProgramEffectOp.ResolveHappyWeiYanEzhi or SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn || receipt.Stage == HappyWeiYanStage.GujingSelection) return HappyWeiYanAdditionalChoices(frame);
        PromptChoice Choice(string option, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => new(new($"happy-wei-yan.{frame.Id}.{receipt.Stage}.{option}"), label, cards ?? [], targets ?? [], new Dictionary<string, string> { ["program-action"] = "happy-wei-yan", ["option"] = option, ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture) });
        if (receipt.Stage == HappyWeiYanStage.Hand) return GetHand(_players[frame.OwnerSeat]).Select(c => Choice("card:" + c.Id, "置入“骨”：" + c.DisplayName, [c.Id])).ToArray();
        if (receipt.Stage == HappyWeiYanStage.Slot) return HappyBoneAvailableSlots(receipt.Target).Select(s => Choice("slot:" + s, EquipmentCatalog.GetSlotName(s), targets: [receipt.Target])).ToArray();
        if (receipt.Stage == HappyWeiYanStage.DamageTarget) return _players.Where(p => p.IsAlive).OrderBy(p => RelativeTableOrder(frame.OwnerSeat, p.Seat)).Select(p => Choice("target:" + p.Seat, "对“" + p.Name + "”造成1点伤害", targets: [p.Seat])).ToArray();
        return [];
    }
    [ProgramAnswer("happy-wei-yan")] private void AnswerHappyWeiYan(PromptChoice chosen)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Happy Wei Yan lost his original choice frame."); RequireHappyWeiYan(frame);
        if (!HappyWeiYanChoices(frame).Any(c => AssistedChoicesEqual([c], [chosen]))) throw new InvalidOperationException("Happy Wei Yan requires the exact current published choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.ResolvingEffect }); var receipt = frame.HappyWeiYan!;
        if (receipt.Operation is SkillProgramEffectOp.ResolveHappyWeiYanEzhi or SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn || receipt.Stage == HappyWeiYanStage.GujingSelection) { AnswerHappyWeiYanAdditional(frame, chosen); return; }
        if (receipt.Stage == HappyWeiYanStage.Hand) { ReplaceRuntimeTop(frame with { HappyWeiYan = receipt with { CardId = chosen.Cards.Single(), Stage = HappyWeiYanStage.Slot } }); ContinueHappyWeiYan(frame.Id); return; }
        if (receipt.Stage == HappyWeiYanStage.Slot)
        {
            var slot = Enum.Parse<EquipmentSlot>(chosen.Parameters["option"][5..]);
            ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt with { Slot = slot } });
            var occupied = GetEquipment(receipt.Target).Where(c => (HappyBoneSlot(c) ?? EquipmentCatalog.Get(c.Kind).Slot) == slot).ToArray();
            if (occupied.Length >= _players[receipt.Target].EquipmentSlotCapacity(slot)) { PayHappyWeiYanMove(frame, HappyWeiYanStage.Replacing, occupied[0], CardLocation.Equipment(receipt.Target), CardLocation.DiscardPile, HappyBoneReplace); ContinueHappyWeiYan(frame.Id); return; }
            PlaceHappyBone(frame); return;
        }
        var target = chosen.Targets.Single(); ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt with { Stage = HappyWeiYanStage.Damage, Target = target } });
        BeginProgramSkillDamage(frame, target, 1);
    }
    private void PlaceHappyBone(ProgramSkillFrame frame)
    {
        var receipt = frame.HappyWeiYan!;
        if (receipt.Slot is not { } slot || !_players[receipt.Target].IsAlive || !HappyBoneAvailableSlots(receipt.Target).Contains(slot) || !GetHand(_players[frame.OwnerSeat]).Any(c => c.Id == receipt.CardId)) { FinishHappyWeiYan(frame); return; }
        var card = GetAdvancedCard(receipt.CardId); var before = _movementSequence;
        ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null), HappyWeiYan = receipt with { Stage = HappyWeiYanStage.Placing, Before = before, After = before, Paid = [] } });
        MoveCard(card, CardLocation.Hand(frame.OwnerSeat), CardLocation.Equipment(receipt.Target), new(HappyBoneEnter), movement =>
        {
            if (movement.To != CardLocation.Equipment(receipt.Target)) throw new InvalidOperationException("A bone placement must enter its selected real equipment slot.");
            var owner = _players[frame.OwnerSeat]; owner.HappyWeiYanBones = Array.AsReadOnly(owner.HappyWeiYanBones.Append(new(card.Id, card.Kind, receipt.Target, slot, receipt.Source, receipt.GameplayHash, frame.Id, movement.Sequence)).ToArray());
            AdvanceEventRulesAndQueueFact(new HappyWeiYanBonePlacedEvent(frame.Id, receipt.Source, receipt.Target, slot, before, movement.Sequence)); SaveHappyWeiYanMove(frame.Id, HappyWeiYanStage.Placing, before);
        }, disclosure: CardMovementDisclosure.FaceDown);
        ContinueHappyWeiYan(frame.Id);
    }
    private void PayHappyWeiYanMove(ProgramSkillFrame frame, HappyWeiYanStage stage, Card card, CardLocation from, CardLocation to, string reason)
    {
        var before = _movementSequence;
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null), HappyWeiYan = frame.HappyWeiYan! with { Stage = stage, Before = before, After = before, Paid = [] } });
        MoveCard(card, from, to, new(reason), _ => SaveHappyWeiYanMove(frame.Id, stage, before));
    }
    private void SaveHappyWeiYanMove(long id, HappyWeiYanStage stage, int before)
    {
        var frame = GetActiveProgramFrame(id); var paid = _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence).ToArray();
        ReplaceRuntimeTop(frame with { HappyWeiYan = frame.HappyWeiYan! with { After = _movementSequence, Paid = paid } });
        AdvanceEventRulesAndQueueFact(new HappyWeiYanMovementEvent(id, stage, before, _movementSequence, paid.Length));
    }
    private void FinishHappyWeiYan(ProgramSkillFrame frame)
    {
        if (frame.PendingMovementContinuation is not null || frame.AttackAttempt is not null) throw new InvalidOperationException("Happy Wei Yan cannot retire a paid native child.");
        ReplaceRuntimeTop(frame = frame with { HappyWeiYan = frame.HappyWeiYan! with { Stage = HappyWeiYanStage.Finished } }); AdvanceEventRulesAndQueueFact(new HappyWeiYanCompletedEvent(frame.Id)); FinishProgramSkill(frame, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 42300)] private bool ReturnHappyWeiYanMovement(ProgramSkillFrame frame) => frame.HappyWeiYan is not null && ContinueHappyWeiYan(frame.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyWeiYanZigu)] private PromptChoice HappyZiguAi(PendingDecision decision) => decision.Choices.First();
}
