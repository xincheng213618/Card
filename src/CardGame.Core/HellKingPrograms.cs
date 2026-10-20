using System.Globalization;

namespace CardGame.Core;

internal interface IHellKingProgramHost
{
    SkillProgramStepOutcome ExecuteHellKing(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal abstract class HellKingOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new HellKingEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
}

internal abstract class HellKingBindingDescriptor : HellKingOperationDescriptor
{
    protected virtual bool ReplacesTarget => false;
    protected virtual bool SelectsTarget => true;
    protected virtual IReadOnlyList<string> Choices => ["eligible", "none"];
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Array.AsReadOnly((SelectsTarget ? new ProgramResourceOperation[] { ReplacesTarget ? new ReplaceSingleTarget() : new SelectSingleTarget() } : [])
            .Append(new CreateChoiceResult(effect.ResultBind!, Choices)).ToArray());
}
internal sealed class BindHellRandomOtherDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindHellRandomOther; }
internal sealed class BindHellOtherDamageSourceDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindHellOtherDamageSource; }
internal sealed class BindHellPeachPoisonTargetDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindHellPeachPoisonTarget; }
internal sealed class ChooseHellLowestHpOtherDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHellLowestHpOther; public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; }
internal sealed class ChooseHellHighestHpOtherDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHellHighestHpOther; public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; }
internal sealed class ChooseHellNextHighestHpOtherDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHellNextHighestHpOther; protected override bool ReplacesTarget => true; public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; }
internal sealed class ChooseHellReincarnationTargetDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHellReincarnationTarget; public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; }
internal sealed class RollHellElementDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RollHellElement; protected override bool SelectsTarget => false; protected override IReadOnlyList<string> Choices => ["fire", "thunder", "none"]; }
internal sealed class RollHellAssaultDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RollHellAssault; protected override bool SelectsTarget => false; protected override IReadOnlyList<string> Choices => ["nanman", "wanjian"]; }
internal sealed class BindHellRepeatedDamageTargetDescriptor : HellKingBindingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindHellRepeatedDamageTarget; }
internal sealed class DiscardHellRandomOtherMountDescriptor : HellKingOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardHellRandomOtherMount; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)]; }
internal sealed class RewardHellRecoverySourceDescriptor : HellKingOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardHellRecoverySource; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHpRecovered)]; }
internal sealed class DiscardHellOutsideDrawGainDescriptor : HellKingOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardHellOutsideDrawGain; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)]; }
internal sealed class ExchangeHellReincarnationHpDescriptor : HellKingOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeHellReincarnationHp;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        if (e.Condition.Kind != SkillProgramConditionKind.ChoiceIs) throw new InvalidOperationException("Reincarnation requires its eligible named-target branch.");
        return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class UseHellPreparationAlcoholDescriptor : HellKingOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseHellPreparationAlcohol; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)]; }
internal sealed class SetHellOthersChainedDescriptor : HellKingOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetHellOthersChained;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class SetHellAllLivingChainedDescriptor : HellKingOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetHellAllLivingChained;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied)];
}
internal sealed class TurnHellFaceUpKillerDescriptor : HellKingOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.TurnHellFaceUpKiller; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied)]; }
internal sealed class DrawHellPreparationDescriptor : HellKingOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawHellPreparation; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)]; }
internal sealed class HellKingEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => host.GetCapability<IHellKingProgramHost>().ExecuteHellKing(frame, effect);
}

internal sealed class HellFuduCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "hell:fudu";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "poison-on-other-peach-use" || t.Window != SkillProgramTriggerWindow.CardUseCommitted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer ||
            t.Optional || !t.IncludeResponseUses || t.Subject is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Priority != 0 || t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null ||
            t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null || t.OrderGroup is not null || t.CardKinds is not [CardKind.Peach] ||
            t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 ||
            t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null ||
            t.DamageOccurrence is not null || t.MovementOccurrence is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.GainPhaseQualification is not null ||
            t.AllowNoEventTarget || t.NoDyingAtActivation || t.DeferredTurnEndOnly || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution ||
            t.IgnoreOwnSkillMovements || t.MovementDiscardOnly || t.SingleActionInstance ||
            t.Effects is not [{ Op: SkillProgramEffectOp.BindHellPeachPoisonTarget, Target: SkillProgramEffectTarget.Owner, ResultBind: "victim", Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.LoseHp, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "victim", OptionId: "eligible" } }])
            throw new InvalidOperationException("Fudu requires its exact forced observer Peach committed-Use, including rescue Uses, followed only by the selected participant's native HP loss.");
    }
}

// Scalars only: the random draw is committed once before any resulting child.
public sealed record HellKingChoiceRecordedEvent(long ProgramFrameId, int InstructionIndex, int OwnerSeat,
    string ResultBind, string OptionId, int? TargetSeat, int ActualTurn) : IGameEvent;
public sealed record HellFanshiDamageCountedEvent(int ActualTurn, int OwnerSeat, long DamageFrameId) : IGameEvent;
public sealed record HellCurrentHpExchangedEvent(long ProgramFrameId, int OwnerSeat, int OtherSeat,
    int OwnerBefore, int OtherBefore, int OwnerAfter, int OtherAfter) : IGameEvent;
public sealed record HellRandomCardDiscardedEvent(long ProgramFrameId, int OwnerSeat, int CardOwnerSeat, int CardId) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class HellKingProgramHost(GameEngine engine) : IHellKingProgramHost
    {
        public SkillProgramStepOutcome ExecuteHellKing(ProgramSkillFrame frame, SkillProgramEffect effect) => engine.ExecuteHellKing(frame, effect);
    }

    private SkillProgramStepOutcome ExecuteHellKing(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var owner = _players[frame.OwnerSeat];
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        switch (effect.Op)
        {
            case SkillProgramEffectOp.BindHellRandomOther:
                BindHellParticipant(frame, effect, RandomHellSeat(HellOtherSeats(frame.OwnerSeat))); break;
            case SkillProgramEffectOp.BindHellOtherDamageSource:
                if (frame.WindowContext?.Window != SkillProgramTriggerWindow.AfterDamageApplied)
                    throw new InvalidOperationException("Abi requires the actual applied-damage window.");
                BindHellParticipant(frame, effect, frame.WindowContext.SourceSeat is { } source && source != frame.OwnerSeat && _players[source].IsAlive ? source : null); break;
            case SkillProgramEffectOp.BindHellPeachPoisonTarget:
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { EffectiveKind: CardKind.Peach } peach })
                    throw new InvalidOperationException("Poison requires a real committed Peach Use.");
                BindHellParticipant(frame, effect, peach.ActorSeat == frame.OwnerSeat ? null : RandomHellSeat(HellOtherSeats(frame.OwnerSeat).Where(s => s != peach.ActorSeat).ToArray())); break;
            case SkillProgramEffectOp.ChooseHellLowestHpOther:
            case SkillProgramEffectOp.ChooseHellHighestHpOther:
            case SkillProgramEffectOp.ChooseHellNextHighestHpOther:
            case SkillProgramEffectOp.ChooseHellReincarnationTarget:
                return ChooseHellExtremeParticipant(frame, effect);
            case SkillProgramEffectOp.RollHellElement:
                BindHellChoice(frame, effect, null, frame.SelectedTargetSeats.Count == 0 ? "none" : _random.Next(2) == 0 ? "fire" : "thunder", selectsTarget: false); break;
            case SkillProgramEffectOp.RollHellAssault:
                BindHellChoice(frame, effect, null, _random.Next(2) == 0 ? "nanman" : "wanjian", selectsTarget: false); break;
            case SkillProgramEffectOp.BindHellRepeatedDamageTarget:
            {
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, DamageFrameId: { } damage })
                    throw new InvalidOperationException("Fanshi requires one actual damage instance.");
                if (ProgramEventHistory<HellFanshiDamageCountedEvent>().Any(e => e.OwnerSeat == frame.OwnerSeat && e.DamageFrameId == damage))
                    throw new InvalidOperationException("Fanshi cannot count the same damage twice.");
                var later = ProgramEventHistory<HellFanshiDamageCountedEvent>().Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == frame.OwnerSeat);
                AdvanceEventRulesAndQueueFact(new HellFanshiDamageCountedEvent(_turnNumber, frame.OwnerSeat, damage));
                BindHellParticipant(frame, effect, later ? RandomHellSeat(HellOtherSeats(frame.OwnerSeat)) : null); break;
            }
            case SkillProgramEffectOp.DiscardHellRandomOtherMount:
            {
                var cards = _players.Where(p => p.IsAlive && p.Seat != frame.OwnerSeat).SelectMany(p => GetEquipment(p)
                    .Where(c => EquipmentCatalog.Get(c.Kind).Slot is (EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse) &&
                        !IsSelfHandCategoryDiscardForbidden(p.Seat, c, CardLocation.Equipment(p.Seat), OwnedCardMoveIntent.Discard))
                    .Select(c => (Card: c, Owner: p.Seat))).ToArray();
                if (cards.Length == 0) break;
                var item = cards[_random.Next(cards.Length)];
                MoveLogicalDiscard(frame.OwnerSeat, item.Owner, item.Card, CardLocation.Equipment(item.Owner), reason, parentFrameId: frame.Id);
                AdvanceEventRulesAndQueueFact(new HellRandomCardDiscardedEvent(frame.Id, frame.OwnerSeat, item.Owner, item.Card.Id)); break;
            }
            case SkillProgramEffectOp.RewardHellRecoverySource:
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterHpRecovered, HpChange: { Kind: HpChangeKind.Recovery } recovery } || recovery.TargetSeat != frame.OwnerSeat)
                    throw new InvalidOperationException("Enyuan requires its actual owner recovery.");
                if (recovery.SourceSeat is { } healer && healer != frame.OwnerSeat && _players[healer].IsAlive)
                    return BeginOperationProgramDraw(frame, healer, 1, reason);
                break;
            case SkillProgramEffectOp.DrawHellPreparation:
                return BeginOperationProgramDraw(frame, frame.OwnerSeat, 2, reason);
            case SkillProgramEffectOp.DiscardHellOutsideDrawGain:
            {
                if (frame.WindowContext is not { TargetSeat: { } gainer } context ||
                    !IsHellZhiwangGain(context.MovementBatch, frame.OwnerSeat, gainer))
                    throw new InvalidOperationException("Zhiwang lost its exact outside-draw hand-gain batch.");
                var hand = GetHand(_players[gainer]).Where(c => !IsSelfHandCategoryDiscardForbidden(gainer, c, CardLocation.Hand(gainer), OwnedCardMoveIntent.Discard)).ToArray();
                if (!_players[gainer].IsAlive || hand.Length == 0) break;
                var card = hand[_random.Next(hand.Length)];
                MoveLogicalDiscard(frame.OwnerSeat, gainer, card, CardLocation.Hand(gainer), reason, parentFrameId: frame.Id);
                AdvanceEventRulesAndQueueFact(new HellRandomCardDiscardedEvent(frame.Id, frame.OwnerSeat, gainer, card.Id)); break;
            }
            case SkillProgramEffectOp.ExchangeHellReincarnationHp:
            {
                if (frame.SelectedTargetSeats is not [var other] || !owner.IsAlive || !_players[other].IsAlive || !owner.HasHpValue || !_players[other].HasHpValue) break;
                var peer = _players[other]; var ownBefore = owner.Hp; var otherBefore = peer.Hp;
                owner.Hp = Math.Min(owner.MaxHp, otherBefore); peer.Hp = Math.Min(peer.MaxHp, ownBefore);
                RecordHpChange(frame.Id, owner.Seat, owner.Seat, ownBefore, owner.Hp, HpChangeKind.Setting);
                RecordHpChange(frame.Id, owner.Seat, other, otherBefore, peer.Hp, HpChangeKind.Setting);
                AdvanceEventRulesAndQueueFact(new HellCurrentHpExchangedEvent(frame.Id, owner.Seat, other, ownBefore, otherBefore, owner.Hp, peer.Hp)); break;
            }
            case SkillProgramEffectOp.UseHellPreparationAlcohol:
                return BeginHellPreparationAlcohol(frame);
            case SkillProgramEffectOp.SetHellOthersChained:
            case SkillProgramEffectOp.SetHellAllLivingChained:
                foreach (var peer in _players.Where(p => p.IsAlive && (effect.Op == SkillProgramEffectOp.SetHellAllLivingChained || p.Seat != frame.OwnerSeat)).ToArray())
                    SetProgramChainedState(frame, true, peer.Seat);
                break;
            case SkillProgramEffectOp.TurnHellFaceUpKiller:
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } death)
                    throw new InvalidOperationException("Icebound requires its actual owner death.");
                if (death.SourceSeat is { } killer && killer != frame.OwnerSeat && _players[killer].IsAlive && !_players[killer].IsFaceDown)
                    TurnOverProgramTarget(frame.Id, frame.OwnerSeat, killer);
                break;
            default: throw new InvalidOperationException("Unsupported Hell king operation.");
        }
        return SkillProgramStepOutcome.Continue;
    }

    private int[] HellOtherSeats(int owner) => _players.Where(p => p.IsAlive && p.Seat != owner).Select(p => p.Seat).ToArray();
    private int? RandomHellSeat(IReadOnlyList<int> seats) => seats.Count == 0 ? null : seats[_random.Next(seats.Count)];
    private void BindHellParticipant(ProgramSkillFrame frame, SkillProgramEffect effect, int? seat) =>
        BindHellChoice(frame, effect, seat, seat is null ? "none" : "eligible", selectsTarget: true);
    private void BindHellChoice(ProgramSkillFrame frame, SkillProgramEffect effect, int? target, string option, bool selectsTarget)
    {
        if (frame.ChoiceBindings.Any(b => b.Name == effect.ResultBind)) throw new InvalidOperationException("A Hell choice cannot be rolled or selected twice.");
        ReplaceRuntimeTop(frame with { SelectedTargetSeats = selectsTarget ? target is { } actual ? Array.AsReadOnly(new[] { actual }) : [] : frame.SelectedTargetSeats,
            ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(new ProgramChoiceResultBinding(effect.ResultBind!, option, frame.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new HellKingChoiceRecordedEvent(frame.Id, frame.InstructionIndex, frame.OwnerSeat, effect.ResultBind!, option, target, _turnNumber));
    }

    private int[] HellExtremeParticipants(ProgramSkillFrame frame, SkillProgramEffectOp op)
    {
        var candidates = _players.Where(p => p.IsAlive && p.Seat != frame.OwnerSeat && p.HasHpValue);
        if (op == SkillProgramEffectOp.ChooseHellReincarnationTarget)
        {
            if (!_players[frame.OwnerSeat].IsAlive || !_players[frame.OwnerSeat].HasHpValue || _players[frame.OwnerSeat].Hp > 2) return [];
            candidates = candidates.Where(p => p.Hp > 2);
        }
        var all = candidates.ToArray();
        if (all.Length == 0) return [];
        var extreme = op == SkillProgramEffectOp.ChooseHellLowestHpOther ? all.Min(p => p.Hp) : all.Max(p => p.Hp);
        return all.Where(p => p.Hp == extreme).Select(p => p.Seat).ToArray();
    }
    private SkillProgramStepOutcome ChooseHellExtremeParticipant(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var seats = HellExtremeParticipants(frame, effect.Op);
        if (seats.Length <= 1) { BindHellParticipant(frame, effect, seats.Length == 1 ? seats[0] : null); return SkillProgramStepOutcome.Continue; }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "选择一名满足当前体力条件的角色。", [], seats, frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description),
            Choices = Array.AsReadOnly(seats.Select(seat => new PromptChoice(new($"hell-extreme.{frame.Id}.{frame.InstructionIndex}.{seat}"),
                $"选择 {_players[seat].Name}", [], [seat], new Dictionary<string, string> { ["program-action"] = "hell-king-extreme", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture) })).ToArray())
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    [ProgramAnswer("hell-king-extreme")]
    private void AnswerHellExtreme(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A Hell target lost its program parent.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (!IsHellExtremeOp(effect.Op) || _pendingDecision?.PlayerSeat != frame.OwnerSeat || choice.Cards.Count != 0 || choice.Targets is not [var target] ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture) || !HellExtremeParticipants(frame, effect.Op).Contains(target))
            throw new InvalidOperationException("A Hell target lost its exact instruction or current extreme HP.");
        ClearPendingDecision(); BindHellParticipant(frame, effect, target); AdvanceRuntimeProgram(frame.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ChooseHellLowestHpOther, SkillProgramEffectOp.ChooseHellHighestHpOther,
        SkillProgramEffectOp.ChooseHellNextHighestHpOther, SkillProgramEffectOp.ChooseHellReincarnationTarget)]
    private PromptChoice SelectAiHellExtreme(PendingDecision decision) => decision.Choices.OrderBy(c => c.Targets.Single()).First();
    private static bool IsHellExtremeOp(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ChooseHellLowestHpOther or SkillProgramEffectOp.ChooseHellHighestHpOther or SkillProgramEffectOp.ChooseHellNextHighestHpOther or SkillProgramEffectOp.ChooseHellReincarnationTarget;
    private static bool IsHellTargetOp(SkillProgramEffectOp op) => IsHellExtremeOp(op) || op is SkillProgramEffectOp.BindHellRandomOther or SkillProgramEffectOp.BindHellOtherDamageSource or SkillProgramEffectOp.BindHellPeachPoisonTarget or SkillProgramEffectOp.BindHellRepeatedDamageTarget;

    private bool IsHellKingChoiceResult(ProgramSkillFrame frame, ProgramChoiceResultBinding binding)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var producers = plan.Instructions.Take(frame.InstructionIndex).Select((e, i) => (Effect: e, Index: i + 1))
            .Where(p => p.Effect.ResultBind == binding.Name && (IsHellTargetOp(p.Effect.Op) || p.Effect.Op is SkillProgramEffectOp.RollHellElement or SkillProgramEffectOp.RollHellAssault)).ToArray();
        if (producers is not [var producer] || binding.ChooserSeat != frame.OwnerSeat) return false;
        var receipt = ProgramEventHistory<HellKingChoiceRecordedEvent>().SingleOrDefault(e => e.ProgramFrameId == frame.Id && e.InstructionIndex == producer.Index && e.ResultBind == binding.Name);
        return receipt is not null && receipt.OwnerSeat == frame.OwnerSeat && receipt.OptionId == binding.OptionId &&
            (IsHellTargetOp(producer.Effect.Op) ? receipt.OptionId == (receipt.TargetSeat is null ? "none" : "eligible") && (receipt.TargetSeat is null || IsValidPlayerSeat(receipt.TargetSeat.Value) && receipt.TargetSeat != frame.OwnerSeat)
                : receipt.TargetSeat is null && (producer.Effect.Op == SkillProgramEffectOp.RollHellElement ? receipt.OptionId is "fire" or "thunder" or "none" : receipt.OptionId is "nanman" or "wanjian"));
    }
    private bool HasExactHellKingParticipantSelection(ProgramSkillFrame frame)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var producer = plan.Instructions.Take(frame.InstructionIndex).Select((e, i) => (Effect: e, Index: i + 1))
            .LastOrDefault(p => IsHellTargetOp(p.Effect.Op) && frame.ChoiceBindings.Any(b => b.Name == p.Effect.ResultBind));
        if (producer.Effect is null) return false;
        var binding = frame.ChoiceBindings.SingleOrDefault(b => b.Name == producer.Effect.ResultBind);
        if (binding is null || !IsHellKingChoiceResult(frame, binding)) return false;
        var receipt = ProgramEventHistory<HellKingChoiceRecordedEvent>().Single(e => e.ProgramFrameId == frame.Id && e.InstructionIndex == producer.Index && e.ResultBind == binding.Name);
        return frame.SelectedTargetSeats.SequenceEqual(receipt.TargetSeat is { } target ? new[] { target } : []);
    }
    private bool IsHellRandomAssaultBranch(ProgramSkillFrame frame, SkillProgramEffect effect) => effect is
        { Op: SkillProgramEffectOp.UseVirtualOrdinaryTrick, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: { } bind, OptionId: { } option } } &&
        frame.WindowContext?.Window == SkillProgramTriggerWindow.PlayPhaseStarting &&
        ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions.Take(frame.InstructionIndex - 1)
            .Any(e => e.Op == SkillProgramEffectOp.RollHellAssault && e.ResultBind == bind) &&
        frame.ChoiceBindings.SingleOrDefault(b => b.Name == bind) is { } result && result.OptionId == option && IsHellKingChoiceResult(frame, result) &&
        (effect.OutputKind == CardKind.BarbarianAssault && option == "nanman" || effect.OutputKind == CardKind.ArrowBarrage && option == "wanjian");

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 40000)]
    private void AssertHellKingSelection(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.InstructionIndex < 1 || frame.InstructionIndex > plan.Instructions.Count) return;
        var paused = plan.Instructions[frame.InstructionIndex - 1];
        if (!IsHellExtremeOp(paused.Op) || frame.ChoiceBindings.Any(b => b.Name == paused.ResultBind) || _resolutionStack.LastOrDefault()?.Id != frame.Id) return;
        var seats = HellExtremeParticipants(frame, paused.Op);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != frame.OwnerSeat || seats.Length < 2 ||
            prompt.Choices.Count != seats.Length || prompt.Choices.Any(c => c.Cards.Count != 0 || c.Targets is not [var target] || !seats.Contains(target) ||
                c.Parameters.GetValueOrDefault("program-action") != "hell-king-extreme" || c.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture)))
            throw new InvalidOperationException("A Hell extreme-HP chooser lost its exact pending target domain.");
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.RewardHellRecoverySource, SkillProgramEffectOp.DrawHellPreparation)]
    private void ReturnHellNativeDraw(ProgramSkillFrame frame, NativeDrawOutcome outcome)
    {
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var recipient = effect.Op == SkillProgramEffectOp.DrawHellPreparation ? frame.OwnerSeat : frame.WindowContext?.HpChange?.SourceSeat;
        var amount = effect.Op == SkillProgramEffectOp.DrawHellPreparation ? 2 : 1;
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || frame.NativeDraw is not null || recipient is null || outcome.RecipientSeat != recipient || outcome.RequestedCount != amount ||
            outcome.SequenceAfter < outcome.SequenceBefore || outcome.SequenceAfter > OutsidePhaseMovementSequence || outcome.ActualDrawIds.Count > amount ||
            outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count || outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            outcome.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == attempt && e.ParentFrameId == frame.Id &&
                e.RecipientSeat == recipient && e.RequestedCount == amount && e.Replaced == (outcome.Replacement is not null)) != 1)
            throw new InvalidOperationException("A Hell draw lost its exact recipient, operation or typed native return.");
        CompleteProgramDrawCards(frame.Id, recipient.Value, null, SkillProgramCardSetVisibility.Private, new($"skill-program.{frame.SkillId}.{effect.Op}"), 0, outcome.ActualDrawIds);
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool HasExactHellPreparationParent(ProgramSkillFrame frame) => frame.WindowContext is
        { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, TargetSeat: { } actor } context && actor != frame.OwnerSeat && IsValidPlayerSeat(actor) &&
        frame.SelectedTargetSeats.SequenceEqual([actor]) && _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(parent => parent.Id == context.ParentFrameId &&
            parent.OwnerSeat == actor && parent.Window == context.Window && parent.CandidateIndex < parent.Candidates.Count && MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex]));
    private SkillProgramStepOutcome BeginHellPreparationAlcohol(ProgramSkillFrame frame)
    {
        if (!HasExactHellPreparationParent(frame) || frame.VirtualBasicDraft is not null) throw new InvalidOperationException("Bitter Wine requires its exact other-character preparation parent.");
        var actor = _players[frame.SelectedTargetSeats.Single()];
        if (!actor.IsAlive || _winner != Winner.None || actor.HasAlcoholEffect || actor.UsedPlayPhaseAlcoholThisTurn || IsCardUseForbidden(actor.Seat, CardKind.Alcohol, CardActionType.Use) ||
            IsDirectedCardTargetProhibited(actor.Seat, actor.Seat, CardKind.Alcohol) || IsCardTargetProhibited(actor, CardKind.Alcohol, Suit.None)) return SkillProgramStepOutcome.Continue;
        var id = ++_resolutionSequence;
        ReplaceRuntimeTop(frame with { VirtualBasicDraft = new(frame.InstructionIndex, id) });
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, null, CardActionType.Use, actor.Seat, frame.OwnerSeat, null, null, null,
            CardKind.Alcohol, [actor.Seat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        PushRuntimeFrame(new CardUseFrame(id, actor.Seat, 0, CardKind.Alcohol, [actor.Seat], PhysicalCardIds: [])
            { Action = action, VirtualBasicReturn = new(frame.Id, frame.InstructionIndex), FirstOwnPlayUseDistanceUnlimited = HasFirstActualPlayUseDistance(actor) });
        RecordMouZhaJiangAwareAlcoholDebit(id, actor);
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, CardKind.Alcohol, actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, [actor.Seat]));
        RecordYingboCardUse(id, actor.Seat, CardKind.Alcohol); RecordProgramUsedBasicCard(actor.Seat, CardKind.Alcohol); RecordActualPlayPhaseUse(action);
        BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Alcohol)); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool IsExactHellPreparationAlcoholDraft(ProgramSkillFrame frame, ProgramVirtualBasicDraft draft) =>
        draft.InstructionIndex == frame.InstructionIndex && draft.ChildFrameId is not null && HasExactHellPreparationParent(frame) &&
        ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect.Op == SkillProgramEffectOp.UseHellPreparationAlcohol;

    private bool HasHellZhiwangHolder => _contentRegistry.Skills.ContainsKey("hell:zhiwang") && _players.Any(p => p.IsAlive && HasRuntimeSkill(p, "hell:zhiwang"));
    private static bool IsHellZhiwangTrigger(SkillProgramTrigger trigger) => trigger.Window == SkillProgramTriggerWindow.CardsGained && trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardHellOutsideDrawGain);
    private bool IsHellZhiwangMovement(CardMovementBatchContext batch, CardMovementRecord move, int gainer)
    {
        if (move.To != CardLocation.Hand(gainer) || move.From == move.To || !_cardMovements.Contains(move)) return false;
        if (move.From != CardLocation.DrawPile) return true;
        var invocations = ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Where(e => e.OwnerSeat == gainer &&
            e.ParentFrameId == batch.ParentFrameId && e.TurnNumber == batch.TurnNumber && e.Reason == move.Reason &&
            move.Sequence > e.SequenceBefore && move.Sequence <= e.SequenceAfter).ToArray();
        // Taking a particular deck card is a gain, but is not necessarily DrawCards.
        if (invocations.Length == 0) return true;
        if (invocations is not [var actual]) throw new InvalidOperationException("A Hell hand gain belongs to overlapping native draw invocations.");
        return actual.LastBatchId == batch.Id && batch.NativeDrawInvocation is { } proof && proof.InvocationId == actual.InvocationId &&
            proof.OwnerSeat == gainer && proof.LastBatchId == batch.Id && proof.ParentFrameId == batch.ParentFrameId && ValidNativeDrawInvocation(proof) &&
            proof.Materials.Any(m => m.CardId == move.CardId && m.PrintedKind == move.CardKind && m.MovementSequence == move.Sequence);
    }
    private bool IsHellZhiwangGain(CardMovementBatchContext? batch, int owner, int gainer) => batch?.MovementTiming is { Phase: not TurnPhase.Draw } && owner != gainer && IsValidPlayerSeat(gainer) &&
        batch.Movements.Any(m => IsHellZhiwangMovement(batch, m, gainer));
    private IEnumerable<ProgramTriggerCandidate> CollectHellZhiwangCandidates(CardMovementBatchContext batch) => batch.MovementTiming is not { Phase: not TurnPhase.Draw } ? [] :
        _players.Where(p => p.IsAlive).SelectMany(p => CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.CardsGained))
            .Where(c => IsHellZhiwangTrigger(GetProgramTrigger(c))).SelectMany(c => batch.Movements.Where(m => m.To.Zone == CardZoneKind.Hand && m.To.OwnerSeat is { } s &&
                s != c.OwnerSeat && IsHellZhiwangMovement(batch, m, s)).Select(m => m.To.OwnerSeat!.Value).Distinct().Where(s => _players[s].IsAlive).Select(s => c with { OccurrenceIndex = s }));
    private ProgramSkillWindowContext CreateHellZhiwangContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate candidate) =>
        new(SkillProgramTriggerWindow.CardsGained, parent.Id, candidate.OwnerSeat, SourceSeat: candidate.OwnerSeat, TargetSeat: candidate.OccurrenceIndex,
            OccurrenceIndex: candidate.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with
                { MovedCardCount = parent.Batch.Movements.Count(m => m.To == CardLocation.Hand(candidate.OccurrenceIndex) && m.From != m.To) }, MovementBatch: parent.Batch);
    private bool CanRunHellZhiwangCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) => context.Window == SkillProgramTriggerWindow.CardsGained &&
        context.OwnerSeat == candidate.OwnerSeat && context.SourceSeat == candidate.OwnerSeat && context.TargetSeat == candidate.OccurrenceIndex &&
        IsHellZhiwangGain(context.MovementBatch, candidate.OwnerSeat, candidate.OccurrenceIndex) && _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault() is { } parent &&
        parent.Id == context.ParentFrameId && parent.Batch == context.MovementBatch && parent.Candidates.ElementAtOrDefault(parent.CandidateIndex) == candidate;

    // The dead character's original Dying remains below its native death tree.
    // Only this exact prefix may be suspended; any new victim's Dying remains active.
    private bool HasExactHellKingDeathPrefix(ProgramSkillFrame root, out DyingFrame? originalDying, out long? originalAttack)
    {
        originalDying = null; originalAttack = null;
        return root.SkillId is ("hell:wangxiang" or "hell:bingfeng" or "hell:heisheng" or "hell:zhuxin" or "hell:leizhu" or "hell:renao" or "hell:huoxing" or "hell:pingdeng") &&
            HasExactHellDeathOriginalCursor(root, out originalDying, out originalAttack);
    }

    private bool HasExactHellDeathOriginalCursor(ProgramSkillFrame root, out DyingFrame? originalDying, out long? originalAttack, int directDepth = 0)
    {
        originalDying = null; originalAttack = null;
        if (directDepth >= _playerCount || root.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } context ||
            context.OwnerSeat != root.OwnerSeat || context.TargetSeat != root.OwnerSeat || root.TriggerId != root.ActivationId || _players[root.OwnerSeat].IsAlive) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index < 3 || _resolutionStack[index - 1] is not ProgramDeathTriggerWindowFrame window || window.Id != context.ParentFrameId ||
            window.OwnerSeat != root.OwnerSeat || window.KillerSeat != context.SourceSeat || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(root, window.Candidates[window.CandidateIndex]) || GetProgramTrigger(window.Candidates[window.CandidateIndex]).Window != context.Window ||
            _resolutionStack[index - 2] is not DeathFrame death || death.Id != window.DeathFrameId || death.VictimSeat != root.OwnerSeat || death.KillerSeat != window.KillerSeat) return false;
        if (death.ReturnKind == DeathReturnKind.ProgramSkill)
            return _resolutionStack[index - 3] is ProgramSkillFrame causer && HasExactHellWuhunDirectDeath(causer, death) &&
                HasExactHellDeathOriginalCursor(causer, out originalDying, out originalAttack, directDepth + 1);
        if (death.ReturnKind != DeathReturnKind.Dying || _resolutionStack[index - 3] is not DyingFrame dying || dying.Id != death.ParentFrameId ||
            dying.VictimSeat != death.VictimSeat || dying.KillerSeat != death.KillerSeat || dying.ResponderIndex != dying.ResponderSeats.Count ||
            ProgramEventHistory<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat) != 1) return false;
        if (dying.Continuation == DyingContinuationKind.Damage)
        {
            if (index < 4 || _resolutionStack[index - 4] is not DamageFrame damage || damage.Id != dying.ParentFrameId || damage.TargetSeat != dying.VictimSeat || damage.Amount <= 0) return false;
            var parent = _resolutionStack.Take(index - 4).SingleOrDefault(f => f.Id == damage.ParentFrameId);
            var cardState = parent switch { CardUseFrame card => card.CardAttack, ProgramSkillFrame program => program.CardAttack, JudgmentFrame judgment => judgment.CardAttack, _ => null };
            var paid = parent is ProgramSkillFrame { AttackAttempt: { DamageWasApplied: true } attack, AttackReturn: not null }
                ? attack.TargetSeat == damage.TargetSeat && attack.SourceSeat == damage.SourceSeat && attack.DamageAmount == damage.Amount && dying.KillerSeat == (attack.SourceLess ? null : attack.SourceSeat)
                : cardState is { Active: true, DamageWasApplied: true } cardAttack && cardAttack.TargetSeat == damage.TargetSeat &&
                    cardAttack.SourceSeat == damage.SourceSeat && cardAttack.DamageAmount == damage.Amount && dying.KillerSeat == (cardAttack.IsSourceLess ? null : cardAttack.SourceSeat);
            if (!paid) return false;
            originalAttack = damage.ParentFrameId;
        }
        else if (dying.Continuation == DyingContinuationKind.ProgramSkill)
        {
            if (index < 4 || _resolutionStack[index - 4] is not ProgramSkillFrame loss || loss.Id != dying.ParentFrameId || dying.KillerSeat is not null ||
                !ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e => e.FrameId == loss.Id && e.SkillId == loss.SkillId && e.TargetSeat == dying.VictimSeat && e.Amount > 0 && e.RemainingHp <= 0)) return false;
        }
        else if (dying.Continuation == DyingContinuationKind.AttackHpLoss)
        {
            if (index < 4 || dying.KillerSeat is not null || _resolutionStack[index - 4] is not { } parent || parent.Id != dying.ParentFrameId) return false;
            var cardAttack = parent switch { CardUseFrame card => card.CardAttack, ProgramSkillFrame program => program.CardAttack, JudgmentFrame judgment => judgment.CardAttack, _ => null };
            var attack = parent is ProgramSkillFrame { AttackAttempt: { } programAttack, AttackReturn: not null } ? programAttack : null;
            var target = attack?.TargetSeat ?? cardAttack?.TargetSeat;
            var source = attack?.SourceSeat ?? cardAttack?.SourceSeat;
            var amount = attack?.DamageAmount ?? cardAttack?.DamageAmount;
            if (target != dying.VictimSeat || amount is not > 0 ||
                !ProgramEventHistory<DamageReplacedWithHpLossEvent>().Any(e => e.ParentResolutionId == parent.Id && e.TargetSeat == target &&
                    e.PolicyOwnerSeat == source && e.Amount == amount && e.RemainingHp <= 0) ||
                ProgramEventHistory<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == target && e.KillerSeat is null) != 1) return false;
            originalAttack = parent.Id;
        }
        else return false;
        originalDying = dying; return true;
    }

    private bool HasExactHellWuhunDirectDeath(ProgramSkillFrame causer, DeathFrame death)
    {
        if (causer.Id != death.ParentFrameId || death.KillerSeat is not null || causer.SkillId != "classic:wuhun" || causer.TriggerId != "death-judgment" ||
            causer.InstructionIndex < 1 || !causer.SelectedTargetSeats.SequenceEqual([death.VictimSeat])) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(causer, _contentRegistry.GetSkill(causer.SkillId).Program!).GetPausedInstruction(causer.InstructionIndex).Effect;
        if (effect is not { Op: SkillProgramEffectOp.CauseDeathUnlessBoundCardKind, Target: SkillProgramEffectTarget.SelectedTarget, Condition.Kind: SkillProgramConditionKind.Always, SourceBind: { } bind } ||
            !effect.CardKinds.SequenceEqual([CardKind.Peach, CardKind.PeachGarden]) || causer.CardSetBindings.SingleOrDefault(b => b.Name == bind) is not
                { Visibility: SkillProgramCardSetVisibility.Public, CardIds: [var cardId], SourceLocations: [var from] }) return false;
        var judgment = ProgramEventHistory<JudgmentResolvedEvent>().Where(e => e.ParentResolutionId == causer.Id && e.TargetSeat == death.VictimSeat && e.Reason == "skill.wuhun.death").ToArray();
        return judgment is [var result] && result.CardId == cardId && result.CardKind is { } kind && !effect.CardKinds.Contains(kind) &&
            ProgramEventHistory<JudgmentRequestedEvent>().Count(e => e.ResolutionId == result.ResolutionId && e.ParentResolutionId == causer.Id && e.TargetSeat == death.VictimSeat && e.Reason == result.Reason) == 1 &&
            ProgramEventHistory<ProgramSkillCauseDeathDeclaredEvent>().Count(e => e.FrameId == causer.Id && e.SkillId == causer.SkillId && e.BindingId == causer.TriggerId &&
                e.SourceSeat == causer.OwnerSeat && e.TargetSeat == death.VictimSeat) == 1 &&
            _cardMovements.Any(m => m.CardId == cardId && m.CardKind == kind && m.From == from && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == $"skill-program.{causer.SkillId}.{causer.TriggerId}.judgment-finish");
    }
    private bool IsOriginalDyingSuspendedByHellKing(DyingFrame dying) => !_players[dying.VictimSeat].IsAlive &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(root => HasExactHellKingDeathPrefix(root, out var original, out _) && original?.Id == dying.Id);

    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 10)]
    private bool AllowsHellKingNestedDamage(ProgramSkillFrame frame, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || frame.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != frame.Id || frame.InstructionIndex < 1 ||
            !HasExactHellKingDeathPrefix(frame, out _, out var originalAttack) ||
            originalAttack is { } original && CurrentDamageAttempt?.ResolutionId != original) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op == SkillProgramEffectOp.Damage && effect.CompiledInstruction is DamageProgramInstruction instruction)
        {
            if (amount != instruction.Amount || source != instruction.SourceReference || nature != instruction.Nature || effect.Target != SkillProgramEffectTarget.SelectedTarget ||
                !HasExactHellKingParticipantSelection(frame) || !frame.SelectedTargetSeats.SequenceEqual([target])) return false;
            return effect.Condition.Kind == SkillProgramConditionKind.Always || effect.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: { } bind, OptionId: { } option } &&
                frame.ChoiceBindings.SingleOrDefault(b => b.Name == bind) is { } result && result.OptionId == option && IsHellKingChoiceResult(frame, result);
        }
        if (effect is not { Op: SkillProgramEffectOp.DamageParticipants, TargetKind: SkillProgramTargetKind.OtherLiving, Condition.Kind: SkillProgramConditionKind.Always } ||
            source is not null || nature != effect.DamageNature || !frame.ReexecuteParticipantInstruction) return false;
        var cursor = frame.NumberBindings.SingleOrDefault(b => b.Name == "participant-cursor-" + (frame.InstructionIndex - 1))?.Value ?? 0;
        var seats = Enumerable.Range(0, _playerCount).Where(seat => seat != frame.OwnerSeat).ToArray();
        return cursor > 0 && cursor <= seats.Length && seats[cursor - 1] == target && amount == (cursor == 1 ? effect.Amount : effect.MinimumValue);
    }
}
