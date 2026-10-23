using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string OlShenfu = "ol:shenfu";
    private static CardConversionSource OlShenfuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int[] OlShenfuUsedTargets(int owner, int turn) => ProgramEventHistory<OlShenfuTargetSelectedEvent>()
        .Where(e => e.Source.OwnerSeat == owner && e.ActualTurn == turn).Select(e => e.TargetSeat).Distinct().ToArray();
    [ProgramCapability] private sealed class OlShenZhenJiHost(GameEngine e) : IOlShenZhenJiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginOlShenfu(f); }
    private SkillProgramStepOutcome BeginOlShenfu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OlShenfu is not null || f.InstructionIndex != 1 || f.SkillId != OlShenfu || f.TriggerId is null ||
            f.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || f.OwnerSeat != _currentSeat)
            throw new InvalidOperationException("Shenfu requires its original accepted own-turn ending.");
        var r = new OlShenfuReceipt(OlShenfuSource(f), f.GameplayHash, f.InstructionIndex, _turnNumber, GetHand(_players[f.OwnerSeat]).Count % 2 != 0)
            { Targets = OlShenfuUsedTargets(f.OwnerSeat, _turnNumber) };
        ReplaceRuntimeTop(f = f with { OlShenfu = r });
        AdvanceEventRulesAndQueueFact(new OlShenfuStartedEvent(f.Id, r.Source, r.ActualTurn, r.Odd));
        PublishOlShenfu(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice OlShenfuChoice(ProgramSkillFrame f, string option, string label, int? target = null) =>
        new(new($"ol-shenfu.{f.Id}.{f.OlShenfu!.Ordinal}.{option}"), label, [], target is { } seat ? [seat] : [],
            new Dictionary<string, string> { ["program-action"] = "ol-shenfu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option });
    private IReadOnlyList<PromptChoice> OlShenfuChoices(ProgramSkillFrame f)
    {
        var r = f.OlShenfu!; var choices = new List<PromptChoice>();
        if (r.Stage == OlShenfuStage.HandCard)
        {
            choices.AddRange(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.TargetSeat, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard)
                .Select(c => c with { Id = new($"ol-shenfu.{f.Id}.{r.Ordinal}.card.{c.Id.Value}"),
                    Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "ol-shenfu", ["option"] = "card" } }));
            return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
        }
        if (r.Stage != OlShenfuStage.Choosing) return [];
        foreach (var p in _players.Where(p => p.IsAlive && !r.Targets.Contains(p.Seat) && (!r.Odd || p.Seat != f.OwnerSeat))
            .OrderBy(p => RelativeTableOrder(f.OwnerSeat, p.Seat)))
        {
            if (r.Odd) choices.Add(OlShenfuChoice(f, "thunder:" + p.Seat, "对" + p.Name + "造成1点雷电伤害", p.Seat));
            else
            {
                choices.Add(OlShenfuChoice(f, "draw:" + p.Seat, "令" + p.Name + "摸一张牌", p.Seat));
                if (BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, p.Seat, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard).Count > 0)
                    choices.Add(OlShenfuChoice(f, "discard:" + p.Seat, "弃置" + p.Name + "的一张手牌", p.Seat));
            }
        }
        choices.Add(OlShenfuChoice(f, "finish", "结束神赋"));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private bool OlShenfuSourceCurrent(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private void PublishOlShenfu(ProgramSkillFrame f)
    {
        if (!OlShenfuSourceCurrent(f)) { FinishOlShenfu(f); return; }
        var choices = OlShenfuChoices(f);
        if (choices.Count == 0 || choices.All(c => c.Parameters.GetValueOrDefault("option") == "finish")) { FinishOlShenfu(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            f.OlShenfu!.Stage == OlShenfuStage.HandCard ? "神赋：选择一张手牌；他人的暗手牌仅显示牌位。" : "神赋：选择未指定过的角色，或结束。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ol-shenfu")] private void AnswerOlShenfu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlShenfu: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !OlShenfuSourceCurrent(f) ||
            !OlShenfuChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Shenfu requires its exact unpaid current target or opaque hand slot.");
        AssertOlShenfu(f); ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var option = choice.Parameters["option"];
        if (option == "finish") { FinishOlShenfu(f); return; }
        if (r.Stage == OlShenfuStage.HandCard)
        {
            if (option != "card" || choice.Parameters.GetValueOrDefault("source-zone") != CardZoneKind.Hand.ToString() ||
                choice.Parameters.GetValueOrDefault("card-owner-seat") != r.TargetSeat.ToString(CultureInfo.InvariantCulture) ||
                !int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), out var slot))
                throw new InvalidOperationException("Shenfu lost its actual selected Hand slot.");
            var card = _cardZones.CardsAt(CardLocation.Hand(r.TargetSeat))[slot]; var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { OlShenfu = r with { Stage = OlShenfuStage.Children, MovementBefore = before, MovementAfter = before },
                PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramLogicalDiscard(f, f.OwnerSeat, [card.Id], new("skill.ol-shen-zhen-ji.shenfu.discard"));
            f = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f = f with { OlShenfu = f.OlShenfu! with { MovementAfter = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new OlShenfuDiscardedEvent(f.Id, r.Ordinal, r.TargetSeat, before, _movementSequence));
            ContinueOlShenfu(f.Id); return;
        }
        var target = choice.Targets.Single(); var mode = option.Split(':')[0];
        r = r with { TargetSeat = target, Mode = mode, Ordinal = r.Ordinal + 1, Targets = r.Targets.Append(target).ToArray(),
            KilledByDamage = false, DrawOutcome = null, MovementBefore = 0, MovementAfter = 0,
            Stage = mode == "thunder" ? OlShenfuStage.Damaging : mode == "draw" ? OlShenfuStage.Drawing : OlShenfuStage.HandCard };
        ReplaceRuntimeTop(f = f with { OlShenfu = r });
        AdvanceEventRulesAndQueueFact(new OlShenfuTargetSelectedEvent(f.Id, r.Source, r.ActualTurn, r.Ordinal, target, mode));
        if (mode == "thunder") { BeginProgramSkillDamage(f, target, 1, nature: DamageNature.Thunder); return; }
        if (mode == "draw") { BeginOperationProgramDraw(f, target, 1, new("skill.ol-shen-zhen-ji.shenfu.draw")); return; }
        PublishOlShenfu(f);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 40525)] private void CaptureOlShenfuDeath(DyingResolvedEvent fact)
    {
        if (fact.Survived || _resolutionStack.OfType<DyingFrame>().SingleOrDefault(d => d.Id == fact.ResolutionId) is not { Continuation: DyingContinuationKind.Damage } dying ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == dying.ParentFrameId) is not { } damage ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == damage.ParentFrameId) is not { OlShenfu: { Stage: OlShenfuStage.Damaging } r } f ||
            dying.VictimSeat != r.TargetSeat || damage.TargetSeat != r.TargetSeat || damage.SourceSeat != f.OwnerSeat ||
            f.AttackAttempt is not { IsChainPropagation: false } || r.KilledByDamage) return;
        ReplaceRuntimeFrame(f.Id, f with { OlShenfu = r with { KilledByDamage = true } });
        AdvanceEventRulesAndQueueFact(new OlShenfuDeathConfirmedEvent(f.Id, r.Ordinal, r.TargetSeat, damage.Id, dying.Id));
    }
    private bool DrainOlShenfu(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueOlShenfu(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlShenfu: { } r } f || f.Id != id) return;
        if (r.Stage is OlShenfuStage.Choosing or OlShenfuStage.HandCard) { PublishOlShenfu(f); return; }
        if (r.Stage == OlShenfuStage.Drawing || f.AttackAttempt is not null) return;
        if (DrainOlShenfu(id)) return;
        f = GetActiveProgramFrame(id); r = f.OlShenfu!;
        var repeat = r.Odd ? r.KilledByDamage : _players[r.TargetSeat].IsAlive && GetHand(_players[r.TargetSeat]).Count == _players[r.TargetSeat].Hp;
        AdvanceEventRulesAndQueueFact(new OlShenfuFlowResolvedEvent(f.Id, r.Ordinal, r.TargetSeat, r.Mode!, repeat));
        if (!repeat || !OlShenfuSourceCurrent(f)) { FinishOlShenfu(f); return; }
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, OlShenfu = r with { Stage = OlShenfuStage.Choosing } });
        PublishOlShenfu(f);
    }
    private void FinishOlShenfu(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { OlShenfu = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40525)] private bool ResumeOlShenfu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlShenfu: not null } f || f.Id != id) return false; ContinueOlShenfu(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40525)] private bool ReturnOlShenfuMovement(ProgramSkillFrame f) => f.OlShenfu is not null && ResumeOlShenfu(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveOlShenZhenJiShenfu)] private void ReturnOlShenfuDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.OlShenfu is not { Stage: OlShenfuStage.Drawing, Mode: "draw" } r || outcome.RecipientSeat != r.TargetSeat || outcome.RequestedCount != 1 ||
            ProgramEventHistory<OlShenfuDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Ordinal == r.Ordinal))
            throw new InvalidOperationException("Shenfu requires its exact one-card native producer return.");
        ReplaceRuntimeTop(f with { OlShenfu = r with { Stage = OlShenfuStage.Children, DrawOutcome = outcome } });
        AdvanceEventRulesAndQueueFact(new OlShenfuDrawReturnedEvent(f.Id, r.Ordinal, r.TargetSeat, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter));
        ResumeOlShenfu(f.Id);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 40525)] private bool OlShenfuMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.OlShenfu is { Stage: OlShenfuStage.Children, Mode: "discard" } && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40525)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40525)]
    private bool RetainIssuedOlShenfu(ProgramSkillFrame f) => f.OlShenfu is not null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveOlShenZhenJiShenfu)] private PromptChoice ChooseOlShenfu(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 40525)] private void AssertOlShenfu(ProgramSkillFrame f)
    {
        if (f.OlShenfu is not { } r) return;
        if (r.Source != OlShenfuSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != 1 || f.InstructionIndex != r.InstructionIndex ||
            r.ActualTurn != _turnNumber || f.OwnerSeat != _currentSeat || !Enum.IsDefined(r.Stage) || r.Ordinal < 0 ||
            r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(t => !IsValidPlayerSeat(t) || r.Odd && t == f.OwnerSeat) ||
            !r.Targets.ToHashSet().SetEquals(OlShenfuUsedTargets(f.OwnerSeat, r.ActualTurn)) ||
            r.MovementBefore < 0 || r.MovementAfter < r.MovementBefore ||
            ProgramEventHistory<OlShenfuStartedEvent>().Count(e => e == new OlShenfuStartedEvent(f.Id, r.Source, r.ActualTurn, r.Odd)) != 1)
            throw new InvalidOperationException("Shenfu lost its original ending, fixed branch or once-per-turn targets.");
        if (r.Ordinal > 0 && (ProgramEventHistory<OlShenfuTargetSelectedEvent>().Count(e => e.FrameId == f.Id && e.Ordinal == r.Ordinal &&
            e.Source == r.Source && e.TargetSeat == r.TargetSeat && e.Mode == r.Mode && e.ActualTurn == r.ActualTurn) != 1 ||
            r.Odd != (r.Mode == "thunder") || !r.Targets.Contains(r.TargetSeat)))
            throw new InvalidOperationException("Shenfu lost its accepted flow target or mode.");
        if (r.KilledByDamage != ProgramEventHistory<OlShenfuDeathConfirmedEvent>().Any(e => e.FrameId == f.Id && e.Ordinal == r.Ordinal && e.TargetSeat == r.TargetSeat))
            throw new InvalidOperationException("Shenfu cannot repeat a thunder flow without the exact native dying result.");
        if (r.Stage == OlShenfuStage.Children && r.Mode == "discard" && ProgramEventHistory<OlShenfuDiscardedEvent>()
            .Count(e => e.FrameId == f.Id && e.Ordinal == r.Ordinal && e.TargetSeat == r.TargetSeat && e.Before == r.MovementBefore && e.After == r.MovementAfter) != 1)
            throw new InvalidOperationException("Shenfu lost its once-paid logical discard.");
    }
}
