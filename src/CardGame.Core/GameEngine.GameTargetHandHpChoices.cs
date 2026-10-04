using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string GameHandHpUsage(string state, int target) => $"game-hand-hp:{state}:target:{target}";
    // The issued public fact is the durable whole-game ledger. ResetSkill may
    // clear ordinary usages; it cannot erase an already issued target action.
    private bool HasIssuedGameHandHpTarget(int owner, string skill, string state, int target) =>
        CompleteProgramEventHistory().OfType<GameTargetHandHpChoiceIssuedEvent>().Any(e => e.OwnerSeat == owner && e.SkillId == skill && e.StateId == state && e.TargetSeat == target);
    private bool CanSelectGameHandHpTarget(int owner, string skill, string state, int target) => IsValidPlayerSeat(target) &&
        _players[owner].IsAlive && _players[target].IsAlive && GetHand(_players[target]).Count != _players[target].Hp && !HasIssuedGameHandHpTarget(owner, skill, state, target);
    private static string GameHandHpReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.ResolveGameTargetHandHpChoice}";
    private Card[] GameHandHpDiscardCards(ProgramSkillFrame f) => GetHand(_players[f.GameTargetHandHp!.TargetSeat]).Concat(GetEquipment(f.GameTargetHandHp.TargetSeat))
        .Where(c => !IsForeignEquipmentDiscardPrevented(f.GameTargetHandHp.TargetSeat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
            (f.GameTargetHandHp.TargetSeat != f.OwnerSeat || !IsActiveProgramSourceEquipmentCard(f.OwnerSeat, f.SkillId, f.SkillInstanceId, c))).ToArray();
    private SkillProgramStepOutcome BeginGameTargetHandHp(ProgramSkillFrame supplied, string state)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.GameTargetHandHp is not null || f.SelectedTargetSeats is not [var target] || f.TriggerId is not null ||
            !CanSelectGameHandHpTarget(f.OwnerSeat, f.SkillId, state, target)) throw new InvalidOperationException("One unused game target must have unequal current hand/HP.");
        var hand = GetHand(_players[target]).Count; var hp = _players[target].Hp; var draw = hand < hp;
        var usage = GameHandHpUsage(state, target);
        if (!_skillRuntimeState.TryConsumeUsage(f.OwnerSeat, f.SkillId, usage, SkillUsageScope.Game, 1))
            throw new InvalidOperationException("The exact game-target opportunity was already consumed.");
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(f.OwnerSeat, f.SkillId, usage, SkillUsageScope.Game, 1));
        ReplaceRuntimeTop(f = f with { GameTargetHandHp = new(f.InstructionIndex, state, target, hand, hp, draw, 0, "choosing", [], []) });
        AdvanceEventRulesAndQueueFact(new GameTargetHandHpChoiceIssuedEvent(f.Id, f.OwnerSeat, f.SkillId, state, target, hand, hp, draw));
        if (draw)
        {
            var before = PrivateOfferSequence;
            ReplaceRuntimeTop(f = f with { GameTargetHandHp = f.GameTargetHandHp! with { Stage = "children", SequenceBefore = before, SequenceAfter = before },
                PendingMovementContinuation = new(target, 0, null) });
            var actual = DrawCards(_players[target], 2, true, new(GameHandHpReason(f))).Count;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { GameTargetHandHp = f.GameTargetHandHp! with { SequenceAfter = PrivateOfferSequence, ActualCount = actual } });
            AdvanceEventRulesAndQueueFact(new GameTargetHandHpMovementIssuedEvent(f.Id, target, true, actual, before, PrivateOfferSequence));
            if (!TryDrainFireTargetMovement(f)) ReturnGameTargetHandHpMovement(f); return SkillProgramStepOutcome.AwaitChild;
        }
        var count = Math.Min(2, GameHandHpDiscardCards(f).Length);
        ReplaceRuntimeTop(f = f with { GameTargetHandHp = f.GameTargetHandHp! with { RequiredCount = count } });
        if (count == 0) { FinishProgramSkill(f, true); return SkillProgramStepOutcome.AwaitChild; }
        PublishGameHandHpDiscard(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> GameHandHpChoices(ProgramSkillFrame f) => Array.AsReadOnly(GameHandHpDiscardCards(f)
        .Where(c => !f.GameTargetHandHp!.CardIds.Contains(c.Id)).Select(c => new PromptChoice(new($"game-hand-hp.{f.Id}.{c.Id}"),
            $"弃置【{c.DisplayName}】（{f.GameTargetHandHp!.CardIds.Count + 1}/{f.GameTargetHandHp.RequiredCount}）", [c.Id], [],
            new Dictionary<string, string> { ["program-action"] = "game-hand-hp-discard", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    private void PublishGameHandHpDiscard(ProgramSkillFrame f)
    {
        var choices = GameHandHpChoices(f); var target = f.GameTargetHandHp!.TargetSeat; var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, target, $"【{skill.Name}】请选择真实弃牌。", choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, TargetSeat = target, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[target].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveGameHandHpDiscard(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("No owning game-target choice.");
        if (f.GameTargetHandHp is not { Stage: "choosing", Draw: false } r || !ValidGameHandHpReceipt(f) || !_players[r.TargetSeat].IsAlive ||
            !AssistedChoicesEqual([choice], GameHandHpChoices(f).Where(c => c.Id == choice.Id).ToArray()) || choice.Cards is not [var id])
            throw new InvalidOperationException("The mandatory discard changed its exact target or material.");
        var ids = r.CardIds.Append(id).ToArray(); var locations = r.Locations.Append(_cardZones.GetLocation(id)).ToArray(); ClearPendingDecision();
        if (ids.Length < r.RequiredCount)
        { ReplaceRuntimeTop(f = f with { GameTargetHandHp = r with { CardIds = ids, Locations = locations } }); PublishGameHandHpDiscard(f); return; }
        if (ids.Length != r.RequiredCount || ids.Where((card, n) => _cardZones.GetLocation(card) != locations[n] || !GameHandHpDiscardCards(f).Any(c => c.Id == card)).Any())
            throw new InvalidOperationException("The frozen mandatory discard is no longer completely payable.");
        var before = PrivateOfferSequence;
        ReplaceRuntimeTop(f = f with { GameTargetHandHp = r with { Stage = "children", CardIds = ids, Locations = locations,
            ActualCount = ids.Length, SequenceBefore = before, SequenceAfter = before }, PendingMovementContinuation = new(r.TargetSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, new(GameHandHpReason(f)));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { GameTargetHandHp = f.GameTargetHandHp! with { SequenceAfter = PrivateOfferSequence } });
        AdvanceEventRulesAndQueueFact(new GameTargetHandHpMovementIssuedEvent(f.Id, r.TargetSeat, false, ids.Length, before, PrivateOfferSequence));
        if (!TryDrainFireTargetMovement(f)) ReturnGameTargetHandHpMovement(f);
    }
    private bool ValidGameHandHpReceipt(ProgramSkillFrame f)
    {
        if (f.GameTargetHandHp is not { } r || r.InstructionIndex != f.InstructionIndex || f.SelectedTargetSeats is not [var target] || target != r.TargetSeat ||
            r.HandCount == r.Hp || r.Draw != (r.HandCount < r.Hp) || r.RequiredCount is < 0 or > 2 || r.CardIds.Count != r.Locations.Count ||
            r.CardIds.Distinct().Count() != r.CardIds.Count || GetPausedPrivateOfferEffect(f) is not { Op: SkillProgramEffectOp.ResolveGameTargetHandHpChoice } op || op.StateId != r.StateId) return false;
        var issued = CompleteProgramEventHistory().OfType<GameTargetHandHpChoiceIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (issued is not [var fact] || fact != new GameTargetHandHpChoiceIssuedEvent(f.Id, f.OwnerSeat, f.SkillId, r.StateId, target, r.HandCount, r.Hp, r.Draw) ||
            CompleteProgramEventHistory().OfType<GameTargetHandHpChoiceIssuedEvent>().Count(e => e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.StateId == r.StateId && e.TargetSeat == target) != 1) return false;
        if (r.Stage == "choosing") return !r.Draw && r.CardIds.Count < r.RequiredCount && r.SequenceBefore == 0 && r.SequenceAfter == 0 &&
            r.CardIds.Select((id, n) => (id, n)).All(x => _cardZones.GetLocation(x.id) == r.Locations[x.n] && GameHandHpDiscardCards(f).Any(c => c.Id == x.id));
        if (r.Stage != "children") return false;
        var movements = CompleteProgramEventHistory().OfType<GameTargetHandHpMovementIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (movements is not [var movement] || movement != new GameTargetHandHpMovementIssuedEvent(f.Id, target, r.Draw, r.ActualCount, r.SequenceBefore, r.SequenceAfter)) return false;
        var paid = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.Reason.Value == GameHandHpReason(f)).ToArray();
        return r.Draw ? r.CardIds.Count == 0 && r.ActualCount is >= 0 and <= 2 && paid.Length == r.ActualCount && paid.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(target)) :
            r.CardIds.Count == r.RequiredCount && r.ActualCount == r.RequiredCount && paid.Length == r.RequiredCount && r.CardIds.Select((id, n) => (id, from: r.Locations[n])).All(p =>
                paid.Count(m => m.CardId == p.id && m.From == p.from && p.from.OwnerSeat == target && p.from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                    (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && GetAdvancedCard(p.id).IsGeneralWeapon)) == 1);
    }
    private bool IsGameHandHpMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.GameTargetHandHp is { Stage: "children" } r && effect?.Op == SkillProgramEffectOp.ResolveGameTargetHandHpChoice && ValidGameHandHpReceipt(f) &&
        pending.SubjectSeat == r.TargetSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool ReturnGameTargetHandHpMovement(ProgramSkillFrame f)
    {
        if (f.GameTargetHandHp is null) return false;
        if (f.PendingMovementContinuation is not { } pending || !IsGameHandHpMovement(f, GetPausedPrivateOfferEffect(f), pending)) throw new InvalidOperationException("Invalid game-target movement return.");
        if (TryDrainFireTargetMovement(f)) return true;
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null, GameTargetHandHp = null }); FinishProgramSkill(GetActiveProgramFrame(f.Id), true); return true;
    }
    private bool ResumeGameHandHp(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GameTargetHandHp: { } r } f || f.Id != id) return false;
        if (!ValidGameHandHpReceipt(f)) throw new InvalidOperationException("Invalid whole-game target action receipt.");
        if (r.Stage == "choosing") return true;
        if (!TryDrainFireTargetMovement(f)) ReturnGameTargetHandHpMovement(f); return true;
    }
}
