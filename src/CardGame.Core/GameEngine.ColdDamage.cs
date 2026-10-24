using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ColdDamageOfferedEvent(long FrameId, long AttackId, int SourceSeat,
    int TargetSeat, int Amount) : IGameEvent;
public sealed record ColdDamageChoiceEvent(long FrameId, bool Prevented) : IGameEvent;
public sealed record ColdDamageDiscardedEvent(long FrameId, int Ordinal, int CardId,
    CardLocation From, int MovementSequence) : IGameEvent;
public sealed record ColdDamageCompletedEvent(long FrameId, long AttackId, bool Prevented,
    int PreventedAmount, int DiscardedCount) : IGameEvent;

/// <summary>The direct Cold damage target and the paid sequential discard prefix.</summary>
public sealed record ColdDamageReplacementFrame(long Id, long ParentFrameId, bool CardAttack,
    int SourceSeat, int TargetSeat, int Amount, bool Activated = false,
    bool ChoiceMade = false, ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.ColdDamageReplacement, Step), IProgramChildResolutionFrame
{
    private IReadOnlyList<ColdDamageDiscardedEvent> _discards = Array.AsReadOnly(Array.Empty<ColdDamageDiscardedEvent>());
    public IReadOnlyList<ColdDamageDiscardedEvent> Discards
    { get => _discards; init => _discards = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeLogicalDiscardInvocationDraft? LogicalDiscard { get; init; }
}

public sealed partial class GameEngine
{
    private static readonly CardMoveReason ColdDamageDiscardReason = new("damage.cold.discard");

    private bool TryBeginColdDamageReplacement(IDamageAttempt attack, int amount, DamageNature nature)
    {
        if (nature != DamageNature.Cold || attack.IsChainPropagation || attack.DamageRedirected ||
            attack.IsSourceLess || !_players[attack.SourceSeat].IsAlive || !_players[attack.TargetSeat].IsAlive ||
            GetIceSwordTargetCardCount(_players[attack.TargetSeat], attack.SourceSeat) == 0)
            return false;
        if (attack is CardAttackHandle)
        {
            if (GetCardAttackState(attack.ResolutionId).ColdDamageReplacementAttempted) return false;
            UpdateCardAttackState(attack.ResolutionId, state => state! with { ColdDamageReplacementAttempted = true });
        }
        else
        {
            if (GetProgramAttackState(attack.ResolutionId).ColdDamageReplacementAttempted) return false;
            UpdateProgramAttackState(attack.ResolutionId, state => state with { ColdDamageReplacementAttempted = true });
        }
        if (_resolutionStack.LastOrDefault()?.Id != attack.ResolutionId)
            throw new InvalidOperationException("Cold damage must be offered on its original direct attack frame.");
        var frame = new ColdDamageReplacementFrame(++_resolutionSequence, attack.ResolutionId,
            attack is CardAttackHandle, attack.SourceSeat, attack.TargetSeat, amount);
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new ColdDamageOfferedEvent(frame.Id, frame.ParentFrameId,
            frame.SourceSeat, frame.TargetSeat, frame.Amount));
        ContinueColdDamageReplacement(frame.Id);
        return true;
    }

    private IDamageAttempt ColdDamageAttack(ColdDamageReplacementFrame frame) => frame.CardAttack
        ? new CardAttackHandle(this, frame.ParentFrameId) : new ProgramAttackHandle(this, frame.ParentFrameId);

    private bool HasColdDamageReplacement(long attackId) =>
        _resolutionStack.OfType<ColdDamageReplacementFrame>().Any(f => f.ParentFrameId == attackId);

    private bool HasColdDamagePrompt() => _resolutionStack.LastOrDefault() is ColdDamageReplacementFrame f &&
        _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p && p.PlayerSeat == f.SourceSeat &&
        p.Choices.Count > 0 && p.Choices.All(c => c.Parameters.GetValueOrDefault("program-action") == "native-cold-damage" &&
            c.Parameters.GetValueOrDefault("frame-id") == f.Id.ToString(CultureInfo.InvariantCulture));

    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.BeforeOpening, 5770)]
    [NativeFrameRoute(typeof(ColdDamageReplacementFrame), 5770)]
    private void ContinueColdDamageReplacement(long frameId)
    {
        if (_pendingDecision is not null) return;
        while (_resolutionStack.LastOrDefault() is ColdDamageReplacementFrame frame && frame.Id == frameId)
        {
            if (TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.ColdDamageReplacement) ||
                TryBeginCardsMovedProgramWindow(frame.Id)) return;
            // The previous discard is already paid. A child can change the next
            // available card, but cannot cause that first card to be charged twice.
            if (frame.ChoiceMade && (!frame.Activated || frame.Discards.Count == 2 ||
                !_players[frame.TargetSeat].IsAlive ||
                GetIceSwordTargetCardCount(_players[frame.TargetSeat], frame.SourceSeat) == 0))
            {
                CompleteNativeLogicalDiscardInvocation(frame.LogicalDiscard);
                AdvanceEventRulesAndQueueFact(new ColdDamageCompletedEvent(frame.Id, frame.ParentFrameId,
                    frame.Activated, frame.Activated ? frame.Amount : 0, frame.Discards.Count));
                var attack = ColdDamageAttack(frame);
                PopResolutionFrame(frame.Id, ResolutionFrameKind.ColdDamageReplacement);
                if (frame.Activated) CompleteDamageAttack(attack);
                else if (!ApplyAttackDamage(attack)) CompleteDamageAttack(attack);
                return;
            }
            PublishColdDamageChoice(frame);
            return;
        }
    }

    private IReadOnlyList<PromptChoice> ColdDamageChoices(ColdDamageReplacementFrame frame)
    {
        var target = _players[frame.TargetSeat];
        var choices = new List<PromptChoice>();
        Dictionary<string, string> Parameters(string action) => new()
        {
            ["program-action"] = "native-cold-damage", ["action"] = action,
            ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture)
        };
        PromptChoice Choice(string key, string text, int[] cards, int[] targets, Dictionary<string, string> parameters) =>
            new(new ChoiceId($"cold.{frame.Id}.{frame.Discards.Count}.{key}"), text, cards, targets, parameters);
        if (!frame.ChoiceMade)
        {
            choices.Add(Choice("replace", "防止此次冰伤害，改为依次弃置目标两张牌。", [], [], Parameters("cold-replace")));
            choices.Add(Choice("damage", $"保留对{target.Name}的{frame.Amount}点冰伤害。", [], [], Parameters("cold-damage")));
        }
        else
        {
            for (var slot = 0; slot < GetHand(target).Count; slot++)
            {
                var p = Parameters("cold-discard"); p["target-zone"] = "hand";
                p["slot-index"] = slot.ToString(CultureInfo.InvariantCulture);
                choices.Add(Choice($"hand-{slot}", $"弃置{target.Name}的第{slot + 1}个暗手牌位。", [], [target.Seat], p));
            }
            foreach (var card in GetEquipment(target).Where(c => !IsForeignEquipmentDiscardPrevented(
                         frame.SourceSeat, c, CardLocation.Equipment(target.Seat), OwnedCardMoveIntent.Discard)))
            {
                var p = Parameters("cold-discard"); p["target-zone"] = "equipment";
                choices.Add(Choice($"equipment-{card.Id}", $"弃置{target.Name}装备区的【{card.DisplayName}】。",
                    [card.Id], [target.Seat], p));
            }
        }
        return choices;
    }

    private void PublishColdDamageChoice(ColdDamageReplacementFrame frame)
    {
        var choices = ColdDamageChoices(frame);
        if (choices.Count == 0) throw new InvalidOperationException("Cold replacement lost its available next discard.");
        ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, frame.SourceSeat,
            frame.Activated ? $"冰伤害已防止，请选择依次弃置的第{frame.Discards.Count + 1}张牌。" : "是否用弃牌替代此次冰伤害？",
            frame.Activated ? choices.SelectMany(c => c.Cards).ToArray() : [],
            frame.Activated ? [frame.TargetSeat] : [], SourceSeat: frame.SourceSeat,
            IncomingCard: ColdDamageAttack(frame).EffectiveCardKind)
        {
            PromptId = _players[frame.SourceSeat].IsHuman ? CreatePromptId() : default,
            IsPrivate = true, TargetSeat = frame.TargetSeat, Choices = choices
        };
        _status = _players[frame.SourceSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("native-cold-damage")]
    private void ResolveColdDamageChoice(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ColdDamageReplacementFrame frame || !HasColdDamagePrompt() ||
            !ColdDamageChoices(frame).Any(c => SameNameHandChoicesEqual(c, choice)))
            throw new InvalidOperationException("Cold damage lost its exact private source choice.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.ResolvingEffect });
        var action = choice.Parameters["action"];
        if (!frame.ChoiceMade)
        {
            if (action is not ("cold-replace" or "cold-damage"))
                throw new InvalidOperationException("Cold replacement must be chosen before its paid discards.");
            ReplaceRuntimeTop(frame = frame with { ChoiceMade = true, Activated = action == "cold-replace" });
            AdvanceEventRulesAndQueueFact(new ColdDamageChoiceEvent(frame.Id, frame.Activated));
            ContinueColdDamageReplacement(frame.Id); return;
        }
        var target = _players[frame.TargetSeat];
        Card material;
        if (action != "cold-discard" || !frame.Activated || frame.Discards.Count >= 2)
            throw new InvalidOperationException("Cold damage lost its unpaid sequential discard slot.");
        if (choice.Parameters["target-zone"] == "hand" && choice.Cards.Count == 0 &&
            int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), NumberStyles.None, CultureInfo.InvariantCulture,
                out var slot) && slot >= 0 && slot < GetHand(target).Count) material = GetHand(target)[slot];
        else if (choice.Parameters["target-zone"] == "equipment" && choice.Cards.Count == 1 &&
                 GetEquipment(target).SingleOrDefault(c => c.Id == choice.Cards[0]) is { } equipment) material = equipment;
        else throw new InvalidOperationException("Cold damage selected an unavailable target entity.");
        var from = _cardZones.GetLocation(material.Id);
        if (IsForeignEquipmentDiscardPrevented(frame.SourceSeat, material, from, OwnedCardMoveIntent.Discard))
            throw new InvalidOperationException("Cold damage cannot discard protected equipment.");
        var draft = frame.LogicalDiscard;
        if (TracksNativeLogicalDiscards)
            draft = draft is null
                ? CaptureNativeLogicalDiscardInvocation(frame.SourceSeat, frame.SourceSeat, [material.Id], ColdDamageDiscardReason, frame.Id)
                : draft with { Materials = draft.Materials.Append(new(material.Id, from)).ToArray() };
        ReplaceRuntimeTop(frame = frame with { LogicalDiscard = draft });
        var sequence = _cardMovements.Count;
        MoveCard(material, from, CardLocation.DiscardPile, ColdDamageDiscardReason);
        var movement = _cardMovements.Skip(sequence).Single(m => m.CardId == material.Id && m.From == from && m.Reason == ColdDamageDiscardReason);
        var receipt = new ColdDamageDiscardedEvent(frame.Id, frame.Discards.Count + 1, material.Id, from, movement.Sequence);
        // Keep any recovery attempts enqueued by removing Silver Lion on this frame.
        var current = (ColdDamageReplacementFrame)_resolutionStack.Last();
        ReplaceRuntimeTop(current with { Discards = frame.Discards.Append(receipt).ToArray() });
        AdvanceEventRulesAndQueueFact(receipt);
        ContinueColdDamageReplacement(frame.Id);
    }

    private bool TryChooseColdDamageAi(PendingDecision decision, out PromptChoice selected)
    {
        selected = null!;
        if (!HasColdDamagePrompt() || _resolutionStack.LastOrDefault() is not ColdDamageReplacementFrame frame) return false;
        var (id, thought) = _aiBrains[decision.PlayerSeat].ChooseColdDamageChoice(CreateSnapshot(decision.PlayerSeat),
            frame.TargetSeat, frame.Amount, _thoughtSequence++, PredictPublicIncomingDamage(ColdDamageAttack(frame), decision.PlayerSeat, damageAmount: frame.Amount));
        AddThought(thought); selected = decision.Choices.Single(c => c.Id == id); return true;
    }

    private void AssertColdDamageReplacementFrames()
    {
        foreach (var frame in _resolutionStack.OfType<ColdDamageReplacementFrame>())
        {
            var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
            var attack = ColdDamageAttack(frame);
            var attempted = frame.CardAttack ? GetCardAttackState(frame.ParentFrameId).ColdDamageReplacementAttempted
                : GetProgramAttackState(frame.ParentFrameId).ColdDamageReplacementAttempted;
            if (index < 1 || _resolutionStack[index - 1].Id != frame.ParentFrameId || !attempted ||
                attack.SourceSeat != frame.SourceSeat || attack.TargetSeat != frame.TargetSeat ||
                attack.IsChainPropagation || attack.DamageRedirected || GetDamageNature(attack) != DamageNature.Cold ||
                frame.Amount <= 0 || frame.Amount != attack.DamageAmount ||
                frame.Activated && !frame.ChoiceMade || frame.Discards.Count > 2 ||
                frame.Discards.Count > 0 && !frame.Activated || frame.Discards.Select(d => d.CardId).Distinct().Count() != frame.Discards.Count ||
                frame.Discards.Where((d, i) => d.FrameId != frame.Id || d.Ordinal != i + 1 ||
                    d.From.OwnerSeat != frame.TargetSeat || d.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                    !_cardMovements.Any(m => m.Sequence == d.MovementSequence && m.CardId == d.CardId && m.From == d.From &&
                        m.Reason == ColdDamageDiscardReason && m.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame)).Any() ||
                frame.LogicalDiscard is { } draft && (draft.ParentFrameId != frame.Id || draft.InitiatorSeat != frame.SourceSeat ||
                    draft.ExecutorSeat != frame.SourceSeat || draft.Reason != ColdDamageDiscardReason ||
                    !draft.Materials.Select(m => m.CardId).SequenceEqual(frame.Discards.Select(d => d.CardId))))
                throw new InvalidOperationException("Cold replacement lost its direct attack, paid entity prefix or typed parent.");
            if (_resolutionStack.LastOrDefault()?.Id == frame.Id && _pendingDecision is not null && !HasColdDamagePrompt())
                throw new InvalidOperationException("Cold replacement lost its source-only pending choice.");
        }
    }
}
