using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IShenwuExpandedProgramHost
{
    SkillProgramStepOutcome ExecuteShenwuExpanded(ProgramSkillFrame frame, SkillProgramEffectOp op);
}
internal abstract class ShenwuExpandedOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new ShenwuExpandedEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
}
internal sealed class AddShenwuDamagePoisonDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AddShenwuDamagePoison;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}
internal sealed class ResolveShenwuPoisonDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenwuPoison;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class ResolveShenwuXunyouDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenwuXunyou;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class ResolveShenwuHonglianDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenwuHonglian;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class ResolveShenwuXushiDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenwuXushi;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterTurnedFaceUp)];
}
internal sealed class BindShenwuTurnOwnerDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindShenwuTurnOwner;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow), new SelectSingleTarget()];
}
internal sealed class GateShenwuYanyuDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GateShenwuYanyu;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class BindShenwuEndedTurnOwnerDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindShenwuEndedTurnOwner;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded), new SelectSingleTarget()];
}
internal sealed class RecoverShenwu2020RebirthDescriptor : ShenwuExpandedOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverShenwu2020Rebirth;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.SelfDyingResponse)];
}
internal sealed class ShenwuExpandedEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShenwuExpandedProgramHost>().ExecuteShenwuExpanded(frame, op);
}

public sealed record ShenwuExpandedDamage(int Seat, int Amount);
public sealed record ShenwuExpandedResolution(string Stage, int Amount = 0, int Cursor = 0, int? CardId = null,
    IReadOnlyList<int>? SelectedIds = null, IReadOnlyList<ShenwuExpandedDamage>? Damage = null);
public sealed record ShenwuExpandedRandomEvent(long ProgramFrameId, string SkillId, int Roll,
    int ObtainedCount, IReadOnlyList<ShenwuExpandedDamage> Damage) : IGameEvent;
public sealed record ShenwuTurnEndHealthEvent(int TurnNumber, int OwnerSeat, int Hp) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShenwuExpandedResolution? ShenwuExpanded { get; init; }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class ShenwuExpandedProgramHost(GameEngine engine) : IShenwuExpandedProgramHost
    {
        public SkillProgramStepOutcome ExecuteShenwuExpanded(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.ExecuteShenwuExpanded(frame, op);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27720)]
    private bool AllowsShenwuExpandedTrigger(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId is "shenwu:enemy-tier" or "shenwu:player-tier-2020")
            return _modeDefinition.ShenwuActivity is { Edition: > 0 } activity &&
                (gate.SkillId == "shenwu:enemy-tier" ? !IsShenwuPlayerSlot(gate.OwnerSeat) && activity.EnemyTier >= 2 : IsShenwuPlayerSlot(gate.OwnerSeat) && activity.PlayerTier >= 2);
        if (gate.SkillId is "shenwu:xunyou" or "shenwu:yanyu")
            return gate.WindowContext is null || gate.Context.TargetSeat is { } other && other != gate.OwnerSeat && _players[other].IsAlive;
        if (gate.SkillId == "shenwu:poison") return gate.Owner.Markers.GetValueOrDefault(PlayerMarkerKind.ShenwuPoison) > 0;
        if (gate.SkillId == "shenwu:zhaohuo") return CurrentDamageAttempt is { } damage && GetDamageNature(damage) == DamageNature.Fire;
        if (gate.SkillId != "shenwu:bingxian") return true;
        if (gate.Context.TargetSeat is not { } actor || actor == gate.OwnerSeat) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var end = Array.FindLastIndex(history, fact => fact is TurnEndedEvent turn && turn.ActorSeat == actor && turn.TurnNumber == _turnNumber);
        var start = end < 0 ? -1 : Array.FindLastIndex(history, end, fact => fact is TurnStartedEvent turn && turn.ActorSeat == actor && turn.TurnNumber == _turnNumber);
        return start >= 0 && !history.Skip(start + 1).Take(end - start - 1).OfType<CardUseDeclaredEvent>().Any(use => use.SourceSeat == actor && IsSlashCard(use.CardKind));
    }

    private SkillProgramStepOutcome ExecuteShenwuExpanded(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var owner = _players[frame.OwnerSeat];
        switch (op)
        {
            case SkillProgramEffectOp.AddShenwuDamagePoison:
                if (frame.WindowContext is { SourceSeat: { } source } && source != owner.Seat) AddShenwuPoison(source, owner.Seat, frame.Id);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.ResolveShenwuPoison:
                return ResolveShenwuPoison(frame);
            case SkillProgramEffectOp.ResolveShenwuHonglian:
            case SkillProgramEffectOp.ResolveShenwuXushi:
                return ResolveShenwuRandomDamage(frame, op);
            case SkillProgramEffectOp.ResolveShenwuXunyou:
                return ResolveShenwuXunyou(frame);
            case SkillProgramEffectOp.BindShenwuTurnOwner:
            case SkillProgramEffectOp.BindShenwuEndedTurnOwner:
                if (frame.WindowContext?.TargetSeat is not { } target || target == frame.OwnerSeat || !_players[target].IsAlive)
                    throw new InvalidOperationException("Yanyu requires the exact other actual turn owner.");
                ReplaceRuntimeTop(frame with { SelectedTargetSeats = Array.AsReadOnly(new[] { target }) });
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.GateShenwuYanyu:
                if (frame.JudgmentOutcomeBindings?.LastOrDefault() is not { Suit: Suit.Heart or Suit.Diamond } ||
                    frame.SelectedTargetSeats is not [var subject] || !_players[subject].IsAlive)
                    ReplaceRuntimeTop(frame with { InstructionIndex = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions.Count });
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.RecoverShenwu2020Rebirth:
                if (frame.ShenwuExpanded is null)
                {
                    ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = new("rebirth-recover"), ReexecuteParticipantInstruction = true });
                    DrawProgramCards(frame.Id, owner.Seat, Math.Max(0, Math.Min(owner.MaxHp, 5) - GetHand(owner).Count), null, null,
                        SkillProgramCardSetVisibility.Private, new("shenwu.2020.rebirth-refill"));
                    var outcome = AwaitProgramBoundCardMovements(frame.Id, owner.Seat);
                    if (outcome != SkillProgramStepOutcome.Continue) return outcome;
                    frame = GetActiveProgramFrame(frame.Id);
                }
                ReplaceRuntimeTop(frame with { ShenwuExpanded = null, ReexecuteParticipantInstruction = false });
                RecoverProgramTargetTo(frame.Id, owner.Seat, owner.Seat, SkillProgramNumberExpression.IntegerConstant, owner.MaxHp, true);
                return SkillProgramStepOutcome.Continue;
            default: throw new InvalidOperationException("Unsupported expanded Shenwu operation.");
        }
    }

    private void AddShenwuPoison(int seat, int source, long frameId)
    {
        var target = _players[seat];
        if (!target.IsAlive || ShenwuEnabled(target, "shenwu:duqu")) return;
        var key = (PlayerMarkerKind.ShenwuPoison, source);
        target.MarkerSourceCounts[key] = target.MarkerSourceCounts.GetValueOrDefault(key) + 1;
        var count = target.Markers[PlayerMarkerKind.ShenwuPoison] = target.Markers.GetValueOrDefault(PlayerMarkerKind.ShenwuPoison) + 1;
        const string grant = "activity:shenwu:poison";
        target.SkillGrants.Grant(new(grant, "shenwu:poison", grant, grant));
        _skillBindingIndex.Invalidate(seat);
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frameId, seat, PlayerMarkerKind.ShenwuPoison, 1, count, source, "shenwu.poison"));
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27720)]
    private void ObserveShenwuExpandedFacts(IGameEvent fact)
    {
        if (fact is CardUseDeclaredEvent use && use.CardKind is CardKind.Peach or CardKind.Alcohol)
            foreach (var holder in _players.Where(p => p.IsAlive && p.Seat != use.SourceSeat && HasCardPolicy(p, SkillProgramCardPolicyKind.ShenwuPoisonOnPeachWine)).ToArray())
                AddShenwuPoison(use.SourceSeat, holder.Seat, use.ResolutionId);
        if (fact is TurnEndedEvent turn && _players[turn.ActorSeat].SkillGrants.Grants.Any(grant => grant.SkillId == "shenwu:juyuan"))
            AdvanceEventRulesAndQueueFact(new ShenwuTurnEndHealthEvent(turn.TurnNumber, turn.ActorSeat, _players[turn.ActorSeat].Hp));
    }

    private SkillProgramStepOutcome ResolveShenwuRandomDamage(ProgramSkillFrame frame, SkillProgramEffectOp op)
    {
        if (frame.ShenwuExpanded is null)
        {
            var others = _players.Where(p => p.IsAlive && p.Seat != frame.OwnerSeat).Select(p => p.Seat).ToList();
            var roll = op == SkillProgramEffectOp.ResolveShenwuHonglian ? _random.Next(4) : 0;
            var cards = op == SkillProgramEffectOp.ResolveShenwuHonglian
                ? _cardZones.CardsAt(CardLocation.DrawPile).Where(card => IsRedSuit(EffectiveSuit(_players[frame.OwnerSeat], card))).ToList() : [];
            _random.Shuffle(cards);
            var selected = cards.Take(roll).ToArray();
            if (op == SkillProgramEffectOp.ResolveShenwuHonglian) _random.Shuffle(others);
            var damages = (op == SkillProgramEffectOp.ResolveShenwuHonglian ? others.Take(3 - roll) : others)
                .Select(seat => new ShenwuExpandedDamage(seat, op == SkillProgramEffectOp.ResolveShenwuXushi ? 1 + _random.Next(2) : 1)).ToArray();
            ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = new("damage", Damage: Array.AsReadOnly(damages)), ReexecuteParticipantInstruction = true });
            AdvanceEventRulesAndQueueFact(new ShenwuExpandedRandomEvent(frame.Id, frame.SkillId, roll,
                selected.Length, Array.AsReadOnly(damages)));
            foreach (var card in selected) MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(frame.OwnerSeat), new("shenwu.honglian.obtain"));
            var outcome = AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat);
            if (outcome != SkillProgramStepOutcome.Continue) return outcome;
            frame = GetActiveProgramFrame(frame.Id);
        }
        var resolution = frame.ShenwuExpanded!;
        while (resolution.Cursor < resolution.Damage!.Count)
        {
            var next = resolution.Damage[resolution.Cursor];
            resolution = resolution with { Cursor = resolution.Cursor + 1 };
            ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = resolution, ReexecuteParticipantInstruction = true });
            if (_players[next.Seat].IsAlive)
                return BeginProgramSkillDamage(frame, next.Seat, next.Amount, nature: op == SkillProgramEffectOp.ResolveShenwuHonglian ? DamageNature.Fire : DamageNature.Normal);
        }
        ReplaceRuntimeTop(frame with { ShenwuExpanded = null, ReexecuteParticipantInstruction = false });
        return SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome ResolveShenwuXunyou(ProgramSkillFrame frame)
    {
        if (frame.ShenwuExpanded is null)
        {
            var candidates = _players.Where(p => p.IsAlive && p.Seat != frame.OwnerSeat)
                .Select(p => (Player: p, Cards: GetHand(p).Concat(GetEquipment(p)).Concat(GetJudgment(p)).ToArray()))
                .Where(entry => entry.Cards.Length > 0).ToArray();
            if (candidates.Length == 0) return SkillProgramStepOutcome.Continue;
            var other = candidates[_random.Next(candidates.Length)];
            var card = other.Cards[_random.Next(other.Cards.Length)];
            ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = new("equip", CardId: card.Id), ReexecuteParticipantInstruction = true });
            MoveCard(card, FindOwnedCardLocation(other.Player, card), CardLocation.Hand(frame.OwnerSeat), new("shenwu.xunyou.obtain"));
            var outcome = AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat);
            if (outcome != SkillProgramStepOutcome.Continue) return outcome;
            frame = GetActiveProgramFrame(frame.Id);
        }
        var r = frame.ShenwuExpanded!;
        if (r.Stage == "equip" && r.CardId is { } id && _cardZones.GetLocation(id) == CardLocation.Hand(frame.OwnerSeat))
        {
            var card = GetHand(_players[frame.OwnerSeat]).Single(c => c.Id == id);
            if (EquipmentCatalog.IsEquipment(card.Kind) && _players[frame.OwnerSeat].EquipmentSlotCapacity(EquipmentCatalog.Get(card.Kind).Slot) > 0 &&
                !IsCardUseForbidden(frame.OwnerSeat, card.Kind, CardActionType.Use) && !IsSelfTargetForbiddenAction(_players[frame.OwnerSeat], card.Kind, [frame.OwnerSeat]))
            {
                ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = r with { Stage = "complete" }, ReexecuteParticipantInstruction = true });
                var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
                var use = BeginCardUse(card, frame.OwnerSeat, [], conversionSource: source);
                MoveCard(card, CardLocation.Hand(frame.OwnerSeat), CardLocation.Processing, CardMoveReasons.EquipmentUse);
                if (!TryBeginEquipmentTargetPrograms(_players[frame.OwnerSeat], card, use)) CompleteEquipmentUse(_players[frame.OwnerSeat], card, use);
                return SkillProgramStepOutcome.AwaitChild;
            }
        }
        ReplaceRuntimeTop(frame with { ShenwuExpanded = null, ReexecuteParticipantInstruction = false });
        return SkillProgramStepOutcome.Continue;
    }

    [NativeUseReturn(27720)]
    private void ContinueShenwuXunyouEquipment()
    {
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { ShenwuExpanded.Stage: "complete", SkillId: "shenwu:xunyou" } frame)
        {
            if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
        }
    }

    private SkillProgramStepOutcome ResolveShenwuPoison(ProgramSkillFrame frame)
    {
        var r = frame.ShenwuExpanded;
        if (r is null)
        {
            var count = _players[frame.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.ShenwuPoison);
            if (count == 0) return SkillProgramStepOutcome.Continue;
            ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = r = new("offer", count, SelectedIds: []) });
        }
        if (r.Stage is "offer" or "select")
        {
            PublishShenwuPoisonChoice(frame);
            return SkillProgramStepOutcome.AwaitChoice;
        }
        if (r.Stage == "lose-hp")
        {
            ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = r with { Stage = "remove" }, ReexecuteParticipantInstruction = true });
            var outcome = new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, frame.OwnerSeat, r.Amount);
            if (outcome != SkillProgramStepOutcome.Continue) return outcome;
            frame = GetActiveProgramFrame(frame.Id);
        }
        MutateParticipantMarker(frame, frame.OwnerSeat, PlayerMarkerKind.ShenwuPoison, -1);
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { ShenwuExpanded = null, ReexecuteParticipantInstruction = false });
        return SkillProgramStepOutcome.Continue;
    }

    private Card[] ShenwuPoisonPayable(ProgramSkillFrame frame) => GetHand(_players[frame.OwnerSeat]).Concat(GetEquipment(_players[frame.OwnerSeat]))
        .Where(card => !IsSelfHandCategoryDiscardForbidden(frame.OwnerSeat, card, FindOwnedCardLocation(_players[frame.OwnerSeat], card), OwnedCardMoveIntent.Discard)).ToArray();

    private IReadOnlyList<PromptChoice> ShenwuPoisonChoices(ProgramSkillFrame frame)
    {
        var r = frame.ShenwuExpanded!;
        PromptChoice Choice(string action, string text, int? card = null) => new(new ChoiceId($"shenwu-poison:{frame.Id}:{action}:{card}"), text,
            card is { } id ? [id] : [], [], new Dictionary<string, string>
            { ["program-action"] = "shenwu-poison", ["poison-action"] = action, ["frame-id"] = frame.Id.ToString() });
        if (r.Stage == "offer")
            return (ShenwuPoisonPayable(frame).Length >= r.Amount ? new[] { Choice("discard", $"弃置{r.Amount}张牌") } : [])
                .Append(Choice("lose-hp", $"失去{r.Amount}点体力")).ToArray();
        return ShenwuPoisonPayable(frame).Where(card => !r.SelectedIds!.Contains(card.Id))
            .Select(card => Choice("card", $"弃置【{card.DisplayName}】（{r.SelectedIds!.Count}/{r.Amount}）", card.Id)).ToArray();
    }

    private void PublishShenwuPoisonChoice(ProgramSkillFrame frame)
    {
        var choices = ShenwuPoisonChoices(frame);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, skill.Description, choices.SelectMany(c => c.Cards).ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("shenwu-poison")]
    private void AnswerShenwuPoison(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenwuExpanded: { } r } frame ||
            !ShenwuPoisonChoices(frame).Any(current => current.Id == choice.Id)) throw new InvalidOperationException("Stale snake poison payment.");
        ClearPendingDecision();
        var action = choice.Parameters["poison-action"];
        if (action == "discard")
        {
            ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = r with { Stage = "select" } });
            PublishShenwuPoisonChoice(frame); return;
        }
        if (action == "lose-hp")
        {
            ReplaceRuntimeTop(frame with { ShenwuExpanded = r with { Stage = "lose-hp" }, ReexecuteParticipantInstruction = true });
            AdvanceRuntimeProgram(frame.Id); return;
        }
        var ids = r.SelectedIds!.Append(choice.Cards.Single()).ToArray();
        if (ids.Length < r.Amount)
        {
            ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = r with { SelectedIds = Array.AsReadOnly(ids) } });
            PublishShenwuPoisonChoice(frame); return;
        }
        ReplaceRuntimeTop(frame = frame with { ShenwuExpanded = r with { Stage = "remove", SelectedIds = Array.AsReadOnly(ids) }, ReexecuteParticipantInstruction = true });
        MoveProgramLogicalDiscard(frame, frame.OwnerSeat, ids, new("shenwu.poison.discard"));
        if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenwuPoison)]
    private PromptChoice ChooseAiShenwuPoison(PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.First();
}
