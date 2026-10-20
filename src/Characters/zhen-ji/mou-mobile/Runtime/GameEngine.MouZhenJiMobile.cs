using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouZhenJiLuoshenId = "mou-mobile:luoshen", MouZhenJiObtainReason = "skill-program.mou-mobile:luoshen.obtain", MouZhenJiDiscardReason = "skill-program.mou-mobile:luoshen.discard";
    private bool HasMouZhenJiLuoshenCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouZhenJiLuoshen);
    private SkillProgramStepOutcome StartMouZhenJiLuoshen(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouZhenJiLuoshen is not null || f.SkillId != MouZhenJiLuoshenId || f.TriggerId != "reveal-seated-hands" || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } c || c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat)
            throw new InvalidOperationException("Mou Luoshen requires its actual owner's native preparation trigger.");
        ReplaceRuntimeTop(f = f with { MouZhenJiLuoshen = new(new(f.SkillId, f.TriggerId, f.OwnerSeat, f.SkillInstanceId), f.GameplayHash, c.ParentFrameId, _turnNumber, _turnProgression.OwnerSeat) });
        PublishMouZhenJiLuoshen(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool ValidMouZhenJiLuoshen(ProgramSkillFrame f)
    {
        if (f.MouZhenJiLuoshen is not { } r || r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 || f.WindowContext?.ParentFrameId != r.WindowId || !Enum.IsDefined(r.Stage) ||
            r.Cursor < 0 || r.Cursor > r.Seats.Count || r.Seats.Distinct().Count() != r.Seats.Count || r.Seats.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat) ||
            r.Before < 0 || r.Before > r.After || r.After > _movementSequence) return false;
        if (r.StartingSeat is null) return r.Stage == MouZhenJiLuoshenStage.StartingSeat && r.Seats.Count == 0 && r.Cursor == 0;
        return ProgramEventHistory<MouZhenJiLuoshenStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.TurnNumber == r.TurnNumber &&
            e.TurnOwnerSeat == r.TurnOwnerSeat && e.StartingSeat == r.StartingSeat && e.Count == r.Seats.Count) == 1 &&
            r.Seats.Select((s, i) => new MouZhenJiLuoshenSeatQueuedEvent(f.Id, i, s)).All(e => ProgramEventHistory<MouZhenJiLuoshenSeatQueuedEvent>().Count(v => v == e) == 1);
    }
    private IReadOnlyList<PromptChoice> MouZhenJiLuoshenChoices(ProgramSkillFrame f)
    {
        var r = f.MouZhenJiLuoshen!;
        if (r.Stage == MouZhenJiLuoshenStage.StartingSeat) return Array.AsReadOnly(_players.Where(p => p.IsAlive).Select(p => new PromptChoice(
            new($"mou-zhen-ji.{f.Id}.start.{p.Seat}"), $"从{p.Name}开始依次展示手牌", [], [p.Seat], new Dictionary<string, string>
            { ["program-action"] = "mou-zhen-ji-luoshen", ["branch"] = "start", ["seat"] = p.Seat.ToString(CultureInfo.InvariantCulture) })).ToArray());
        var seat = r.Seats[r.Cursor];
        return Array.AsReadOnly(GetHand(_players[seat]).Select(c => new PromptChoice(new($"mou-zhen-ji.{f.Id}.show.{c.Id}"), $"展示【{CardCatalog.Get(c.Kind).DisplayName}】", [c.Id], [],
            new Dictionary<string, string> { ["program-action"] = "mou-zhen-ji-luoshen", ["branch"] = "reveal", ["card-id"] = c.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    }
    private void PublishMouZhenJiLuoshen(ProgramSkillFrame f)
    {
        var r = f.MouZhenJiLuoshen!; var chooser = r.Stage == MouZhenJiLuoshenStage.StartingSeat ? f.OwnerSeat : r.Seats[r.Cursor];
        var choices = MouZhenJiLuoshenChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MouZhenJiLuoshenStage.StartingSeat ? "洛神：选择起始角色。" : "洛神：展示一张手牌。", choices.SelectMany(c => c.Cards).ToArray(), choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-zhen-ji-luoshen")]
    private void AnswerMouZhenJiLuoshen(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouZhenJiLuoshen!;
        var chooser = r.Stage == MouZhenJiLuoshenStage.StartingSeat ? f.OwnerSeat : r.Seats[r.Cursor];
        if (!ValidMouZhenJiLuoshen(f) || _pendingDecision?.PlayerSeat != chooser ||
            !AssistedChoicesEqual([selected], MouZhenJiLuoshenChoices(f).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Luoshen requires the current actual participant's published choice.");
        ClearPendingDecision();
        if (r.Stage == MouZhenJiLuoshenStage.StartingSeat)
        {
            var start = selected.Targets.Single(); var count = (_players.Count(p => p.IsAlive) + 1) / 2;
            var seats = Enumerable.Range(0, _players.Count).Select(i => (start + i) % _players.Count).Where(s => s != f.OwnerSeat && _players[s].IsAlive).Take(count).ToArray();
            ReplaceRuntimeTop(f = f with { MouZhenJiLuoshen = r with { StartingSeat = start, Seats = seats, Stage = MouZhenJiLuoshenStage.RevealChoice } });
            AdvanceEventRulesAndQueueFact(new MouZhenJiLuoshenStartedEvent(f.Id, r.Source, r.TurnNumber, r.TurnOwnerSeat, start, seats.Length));
            for (var i = 0; i < seats.Length; i++) AdvanceEventRulesAndQueueFact(new MouZhenJiLuoshenSeatQueuedEvent(f.Id, i, seats[i]));
            ContinueMouZhenJiLuoshen(f.Id); return;
        }
        var card = GetAdvancedCard(selected.Cards.Single()); var from = CardLocation.Hand(chooser);
        if (_cardZones.GetLocation(card.Id) != from) throw new InvalidOperationException("Luoshen cannot show a departed hand entity.");
        var suit = EffectiveSuit(_players[chooser], card); var red = SuitColor(suit); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouZhenJiLuoshen = r with { RevealedCardId = card.Id, RevealedSuit = suit, Stage = MouZhenJiLuoshenStage.MovementChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, f.TriggerId!, f.OwnerSeat, "luoshen-reveal", [ToSnapshot(card)]));
        AdvanceEventRulesAndQueueFact(new MouZhenJiLuoshenRevealedEvent(f.Id, r.Cursor, chooser, card.Id, suit));
        if (_cardZones.GetLocation(card.Id) == from && _players[f.OwnerSeat].IsAlive && _players[chooser].IsAlive && _winner == Winner.None)
        {
            if (red == false) MoveCard(card, from, CardLocation.Hand(f.OwnerSeat), new(MouZhenJiObtainReason));
            else if (red == true && !IsSelfHandCategoryDiscardForbidden(chooser, card, from, OwnedCardMoveIntent.Discard))
                MoveLogicalDiscard(f.OwnerSeat, chooser, card, from, new(MouZhenJiDiscardReason));
            var m = _cardMovements.SingleOrDefault(m => m.Sequence > before && m.CardId == card.Id && m.From == from && m.Reason.Value == (red == false ? MouZhenJiObtainReason : MouZhenJiDiscardReason));
            if (m is not null)
            {
                AdvanceEventRulesAndQueueFact(new MouZhenJiLuoshenMovedEvent(f.Id, r.Cursor, card.Id, from, m.To, m.Sequence, red == false));
                if (red == false && m.To == CardLocation.Hand(f.OwnerSeat) && !_turnCardUseEffects.GetHandLimitExemptCardIds(r.TurnNumber, r.TurnOwnerSeat, f.OwnerSeat).Contains(card.Id))
                {
                    var grant = _turnCardUseEffects.GrantHandLimitExemptCard(r.TurnNumber, r.TurnOwnerSeat, f.Id, f.InstructionIndex - 1, 0,
                        CreateProgramTurnEffectSource(f), f.OwnerSeat, card.Id, m.Sequence);
                    AdvanceEventRulesAndQueueFact(new TurnHandLimitExemptCardGrantedEvent(grant));
                }
            }
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MouZhenJiLuoshen = f.MouZhenJiLuoshen! with { After = _movementSequence } });
        ContinueMouZhenJiLuoshen(f.Id);
    }
    private bool DrainMouZhenJiLuoshen(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueMouZhenJiLuoshen(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhenJiLuoshen: { } r } f || f.Id != id) return;
        if (!ValidMouZhenJiLuoshen(f)) throw new InvalidOperationException("Luoshen lost its owning cursor or frozen seat route.");
        if (r.Stage == MouZhenJiLuoshenStage.MovementChildren)
        {
            if (DrainMouZhenJiLuoshen(f)) return;
            f = GetActiveProgramFrame(id); r = f.MouZhenJiLuoshen!;
            ReplaceRuntimeTop(f = f with { MouZhenJiLuoshen = r = r with { Cursor = r.Cursor + 1, Stage = MouZhenJiLuoshenStage.RevealChoice, RevealedCardId = null, RevealedSuit = null }, PendingMovementContinuation = null });
        }
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishMouZhenJiLuoshen(f); return; }
        while (r.Cursor < r.Seats.Count && (!_players[r.Seats[r.Cursor]].IsAlive || GetHand(_players[r.Seats[r.Cursor]]).Count == 0)) r = r with { Cursor = r.Cursor + 1 };
        ReplaceRuntimeTop(f = f with { MouZhenJiLuoshen = r });
        if (r.Cursor >= r.Seats.Count) { FinishMouZhenJiLuoshen(f); return; }
        PublishMouZhenJiLuoshen(f);
    }
    private void FinishMouZhenJiLuoshen(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new MouZhenJiLuoshenCompletedEvent(f.Id, f.MouZhenJiLuoshen!.Cursor));
        ReplaceRuntimeTop(f = f with { MouZhenJiLuoshen = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouZhenJiLuoshen)]
    private PromptChoice AiMouZhenJiLuoshen(PendingDecision p, ProgramSkillFrame f) => f.MouZhenJiLuoshen!.Stage == MouZhenJiLuoshenStage.StartingSeat
        ? p.Choices.OrderBy(c => (c.Targets.Single() - f.OwnerSeat + _players.Count) % _players.Count).First()
        : p.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[p.PlayerSeat])).ThenBy(c => c.Cards.Single()).First();
    [ProgramCapability] private sealed class MouZhenJiLuoshenCapability(GameEngine engine) : IMouZhenJiLuoshenHost
    { public SkillProgramStepOutcome Start(ProgramSkillFrame f) => engine.StartMouZhenJiLuoshen(f); }
}
