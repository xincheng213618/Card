using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string GaoXiangAction = "anniversary-gao-xiang";
    private static CardMoveReason GaoXiangDiscardReason => new("skill-program.anniversary:chiying.discard");
    private static CardMoveReason GaoXiangGainReason => new("skill-program.anniversary:chiying.obtain-basics");
    [ProgramCapability] private sealed class AnniversaryGaoXiangHost(GameEngine engine) : IAnniversaryGaoXiangHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f) => engine.BeginGaoXiang(f); }
    private CardConversionSource GaoXiangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private SkillProgramStepOutcome BeginGaoXiang(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.GaoXiangChiying is not null || f.SkillId != GaoXiangComposition.Skill || f.ActivationId != GaoXiangComposition.Activation ||
            f.TriggerId is not null || f.InstructionIndex != 1 || f.WindowContext is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play)
            throw new InvalidOperationException("Chiying requires its original zero-input once-per-Play activation.");
        var r = new GaoXiangChiyingReceipt(GaoXiangSource(f), f.GameplayHash, _turnNumber, _turnProgression.OwnerSeat);
        ReplaceRuntimeTop(f = f with { GaoXiangChiying = r });
        AdvanceEventRulesAndQueueFact(new GaoXiangChiyingStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner));
        ContinueGaoXiang(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private int GaoXiangHp(ProgramSkillFrame f, int seat) => HappyQiaoQueryValue(f, seat, "gao-xiang-target-hp");
    private int GaoXiangChooser(ProgramSkillFrame f) => f.GaoXiangChiying!.Stage == GaoXiangChiyingStage.Actor ? f.OwnerSeat : f.GaoXiangChiying.Actor;
    private IReadOnlyList<PromptChoice> GaoXiangCardChoices(ProgramSkillFrame f, int victim) =>
        BuildOwnedCardPaymentChoices(f.Id, f.GaoXiangChiying!.Actor, victim, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard);
    private IEnumerable<int> GaoXiangVictimCandidates(ProgramSkillFrame f) => _players.Where(p => p.IsAlive && p.Seat != f.GaoXiangChiying!.Actor &&
        IsWithinAttackRange(f.GaoXiangChiying.Actor, p.Seat) && GaoXiangCardChoices(f, p.Seat).Count > 0).Select(p => p.Seat);
    private IReadOnlyList<PromptChoice> GaoXiangChoices(ProgramSkillFrame f)
    {
        var r = f.GaoXiangChiying!; var list = new List<PromptChoice>();
        PromptChoice Choice(string mode, int seat, string label) => new(new($"gao-xiang.{f.Id}.{r.Stage}.{r.Index}.{mode}.{seat}"), label, [], seat < 0 ? [] : [seat],
            new Dictionary<string, string> { ["program-action"] = GaoXiangAction, ["mode"] = mode });
        if (r.Stage == GaoXiangChiyingStage.Actor)
            foreach (var p in _players.Where(p => p.IsAlive && (p.HasHpValue || p.HappyQiaoHealth is not null) && GaoXiangHp(f, p.Seat) <= GaoXiangHp(f, f.OwnerSeat)))
                list.Add(Choice("actor", p.Seat, "令" + p.Name + "发动驰应弃牌"));
        if (r.Stage == GaoXiangChiyingStage.Victims)
        {
            foreach (var seat in GaoXiangVictimCandidates(f).Where(s => !r.Victims.Contains(s))) list.Add(Choice("victim", seat, "选择弃置" + _players[seat].Name + "的一张牌"));
            list.Add(Choice("finish", -1, r.Victims.Count == 0 ? "不选择任何角色" : "完成选择，依次弃牌"));
        }
        if (r.Stage == GaoXiangChiyingStage.Card && r.Index < r.Victims.Count)
            foreach (var c in GaoXiangCardChoices(f, r.Victims[r.Index]))
                list.Add(c with { Id = new($"gao-xiang.{f.Id}.{r.Index}.{c.Id.Value}"), Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = GaoXiangAction, ["mode"] = "card" } });
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishGaoXiang(ProgramSkillFrame f)
    {
        var choices = GaoXiangChoices(f);
        if (choices.Count == 0) { FinishProgramSkill(f, true); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, GaoXiangChooser(f), choices, "驰应：由被选角色决定弃牌对象和实际区域牌。",
            f.GaoXiangChiying!.Stage == GaoXiangChiyingStage.Card ? f.GaoXiangChiying.Victims[f.GaoXiangChiying.Index] : f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(GaoXiangAction)] private void AnswerGaoXiang(PromptChoice answer)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GaoXiangChiying: { } r } f || !ValidGaoXiang(f) ||
            _pendingDecision?.PlayerSeat != GaoXiangChooser(f) || !AssistedChoicesEqual([answer], GaoXiangChoices(f).Where(c => c.Id == answer.Id).ToArray()))
            throw new InvalidOperationException("Chiying requires the exact current participant and published private card slot.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var mode = answer.Parameters["mode"];
        if (r.Stage == GaoXiangChiyingStage.Actor && mode == "actor")
        {
            var actor = answer.Targets.Single(); var ownHp = GaoXiangHp(f, f.OwnerSeat); var hp = GaoXiangHp(f, actor);
            ReplaceRuntimeTop(f = f with { GaoXiangChiying = r with { Actor = actor, OwnerHp = ownHp, ActorHp = hp, Stage = GaoXiangChiyingStage.Victims } });
            AdvanceEventRulesAndQueueFact(new GaoXiangChiyingActorEvent(f.Id, actor, ownHp, hp)); ContinueGaoXiang(f.Id); return;
        }
        if (r.Stage == GaoXiangChiyingStage.Victims)
        {
            if (mode == "victim")
            { ReplaceRuntimeTop(f = f with { GaoXiangChiying = r with { Victims = r.Victims.Append(answer.Targets.Single()).ToArray() } }); PublishGaoXiang(f); return; }
            if (mode != "finish") throw new InvalidOperationException("Chiying's participant plan cannot be committed by another instruction.");
            var victims = r.Victims.OrderBy(seat => RelativeTableOrder(_currentSeat, seat)).ToArray();
            if (victims.Any(s => !GaoXiangVictimCandidates(f).Contains(s))) throw new InvalidOperationException("Chiying's participant plan lost its real attack-range/card eligibility.");
            ReplaceRuntimeTop(f = f with { GaoXiangChiying = r with { Victims = victims, Committed = true, Stage = GaoXiangChiyingStage.Card } });
            foreach (var (victim, ordinal) in victims.Select((s, i) => (s, i))) AdvanceEventRulesAndQueueFact(new GaoXiangChiyingVictimEvent(f.Id, ordinal, r.Actor, victim));
            AdvanceEventRulesAndQueueFact(new GaoXiangChiyingCommittedEvent(f.Id, r.Actor, victims.Length)); ContinueGaoXiang(f.Id); return;
        }
        if (r.Stage != GaoXiangChiyingStage.Card || mode != "card") throw new InvalidOperationException("Chiying cannot repeat a completed discard.");
        var victimSeat = r.Victims[r.Index];
        var zone = Enum.Parse<CardZoneKind>(answer.Parameters["source-zone"]); var slot = int.Parse(answer.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var from = new CardLocation(zone, victimSeat); var card = _cardZones.CardsAt(from)[slot];
        ReplaceRuntimeTop(f = f with { GaoXiangChiying = r with { Stage = GaoXiangChiyingStage.DiscardChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, r.Actor, [card.Id], GaoXiangDiscardReason, (batch, records) =>
        {
            var m = records.Single(); var active = GetActiveProgramFrame(f.Id);
            var paid = new GaoXiangChiyingMove(r.Index, victimSeat, card.Id, card.Kind, m.From, m.To, m.Sequence, batch);
            ReplaceRuntimeTop(active with { GaoXiangChiying = active.GaoXiangChiying! with { After = _movementSequence, Discards = active.GaoXiangChiying!.Discards.Append(paid).ToArray() } });
            RecordBianFuRenForeignDiscardProducer(m, r.Actor, f.Id);
            AdvanceEventRulesAndQueueFact(GaoXiangMovedEvent(f.Id, false, paid));
        }); ContinueGaoXiang(f.Id);
    }
    private static GaoXiangChiyingMovedEvent GaoXiangMovedEvent(long id, bool gain, GaoXiangChiyingMove m) =>
        new(id, gain, m.Ordinal, m.Victim, m.CardId, m.PrintedKind, m.From, m.To, m.Sequence, m.BatchId);
    private bool DrainGaoXiang(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(f.Id) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 44900)] private bool ContinueGaoXiang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GaoXiangChiying: not null } top || top.Id != id) return false;
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { GaoXiangChiying: { } r } f && f.Id == id)
        {
            RequireGaoXiang(f);
            if (r.Stage is GaoXiangChiyingStage.DiscardChildren or GaoXiangChiyingStage.GainChildren)
            {
                if (DrainGaoXiang(f)) return true;
                f = GetActiveProgramFrame(id); r = f.GaoXiangChiying!;
                if (r.Stage == GaoXiangChiyingStage.GainChildren) { FinishProgramSkill(f with { PendingMovementContinuation = null }, true); return true; }
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, GaoXiangChiying = r = r with { Index = r.Index + 1, Stage = GaoXiangChiyingStage.Card } });
            }
            if (_winner != Winner.None || r.Actor >= 0 && !_players[r.Actor].IsAlive || !r.Committed &&
                (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)))
            { FinishProgramSkill(f, true); return true; }
            if (r.Stage == GaoXiangChiyingStage.HealthQueries)
            {
                if (!_players[f.OwnerSeat].HasHpValue && _players[f.OwnerSeat].HappyQiaoHealth is null) { FinishProgramSkill(f, true); return true; }
                foreach (var p in _players.Where(p => p.IsAlive && (p.HasHpValue || p.HappyQiaoHealth is not null)))
                    if (TryBeginHappyQiaoQuery(GetActiveProgramFrame(id), p.Seat, "gao-xiang-target-hp")) return true;
                f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f = f with { GaoXiangChiying = r = f.GaoXiangChiying! with { Stage = GaoXiangChiyingStage.Actor } });
            }
            if (r.Stage == GaoXiangChiyingStage.Card)
            {
                if (r.Index < r.Victims.Count)
                {
                    if (!_players[r.Victims[r.Index]].IsAlive || GaoXiangCardChoices(f, r.Victims[r.Index]).Count == 0)
                    { ReplaceRuntimeTop(f with { GaoXiangChiying = r with { Index = r.Index + 1 } }); continue; }
                    PublishGaoXiang(f); return true;
                }
                var gain = r.Actor == f.OwnerSeat ? [] : r.Discards.Where(m => m.To == CardLocation.DiscardPile &&
                    GetProgramCardCategory(m.PrintedKind) == SkillProgramCardCategory.Basic && _cardZones.GetLocation(m.CardId) == CardLocation.DiscardPile).ToArray();
                if (gain.Length == 0) { FinishProgramSkill(f, true); return true; }
                ReplaceRuntimeTop(f = f with { GaoXiangChiying = r with { Stage = GaoXiangChiyingStage.GainChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                MoveProgramCardsFromMultipleSources(gain.Select(m => m.CardId).ToArray(), CardLocation.Hand(r.Actor), GaoXiangGainReason, (batch, records) =>
                {
                    var active = GetActiveProgramFrame(id); var gains = records.Select(m =>
                    { var original = gain.Single(x => x.CardId == m.CardId); return new GaoXiangChiyingMove(original.Ordinal, original.Victim, m.CardId, original.PrintedKind, m.From, m.To, m.Sequence, batch); }).ToArray();
                    ReplaceRuntimeTop(active with { GaoXiangChiying = active.GaoXiangChiying! with { After = _movementSequence, Gains = gains } });
                    foreach (var m in gains) AdvanceEventRulesAndQueueFact(GaoXiangMovedEvent(id, true, m));
                }); continue;
            }
            PublishGaoXiang(f); return true;
        }
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 44900)] private bool ReturnGaoXiangMovement(ProgramSkillFrame f) => f.GaoXiangChiying is not null && ContinueGaoXiang(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 44900)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 44900)]
    private bool KeepGaoXiangCommitted(ProgramSkillFrame f) => f.GaoXiangChiying is { Committed: true } && ValidGaoXiang(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryGaoXiangChiying)] private PromptChoice GaoXiangAi(PendingDecision p, ProgramSkillFrame f) =>
        f.GaoXiangChiying!.Stage == GaoXiangChiyingStage.Actor ? p.Choices.FirstOrDefault(c => c.Targets.Contains(f.OwnerSeat)) ?? p.Choices[0] : p.Choices[0];
}
