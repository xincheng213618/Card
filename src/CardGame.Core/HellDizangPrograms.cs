using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IHellDizangProgramHost
{
    SkillProgramStepOutcome ExecuteHellDizang(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}

internal abstract class HellDizangOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new HellDizangEffectHandler(Op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
}

internal sealed class DamageHellBufoAdjacentDescriptor : HellDizangOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageHellBufoAdjacent;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

internal sealed class ChooseHellDayuanJudgmentDescriptor : HellDizangOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHellDayuanJudgment;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.JudgmentBeforeResult)];
}

internal sealed class HellDizangEffectHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host.GetCapability<IHellDizangProgramHost>().ExecuteHellDizang(frame, Op);
}

public sealed record HellBufoReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurnNumber, int NextTargetIndex)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}

public sealed record HellDayuanChoiceReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long WindowFrameId, long JudgmentFrameId, int CardId, Suit? SelectedSuit = null);
public sealed record HellDayuanJudgmentOverride(long ProgramFrameId, long JudgmentFrameId, CardConversionSource Source,
    string GameplayHash, int CardId, Suit Suit, int Rank);
public sealed record HellBufoTargetFrozenEvent(long ProgramFrameId, int OwnerSeat, int TargetSeat, int Index, int Total) : IGameEvent;
public sealed record HellDayuanJudgmentOverriddenEvent(HellDayuanJudgmentOverride Receipt) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HellBufoReceipt? HellBufo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HellDayuanChoiceReceipt? HellDayuan { get; init; }
}

public sealed partial record JudgmentFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HellDayuanJudgmentOverride? HellDayuanOverride { get; init; }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class HellDizangProgramHost(GameEngine engine) : IHellDizangProgramHost
    {
        public SkillProgramStepOutcome ExecuteHellDizang(ProgramSkillFrame frame, SkillProgramEffectOp operation) =>
            engine.ExecuteHellDizang(frame, operation);
    }

    private static CardConversionSource HellDizangSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40080)]
    private bool AllowsHellDizangTrigger(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId == "hell:bufo" && gate.Trigger.Id == "reduce-large-damage")
            return gate.Context.Window == SkillProgramTriggerWindow.BeforeDamageApplied &&
                gate.Context.TargetSeat == gate.OwnerSeat && gate.Context.Amount >= 2;
        if (gate.Trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ChooseHellDayuanJudgment))
            return gate.Trigger.Effects is [{ Op: SkillProgramEffectOp.ChooseHellDayuanJudgment }] &&
                gate.Context.Window == SkillProgramTriggerWindow.JudgmentBeforeResult &&
                gate.Context.Judgment is { } judgment && ActiveJudgment is { } pending &&
                pending.Id == judgment.JudgmentFrameId && GetJudgmentCard(pending)?.Id == judgment.CardId;
        return true;
    }

    private SkillProgramStepOutcome ExecuteHellDizang(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.TriggerId is null || frame.InstructionIndex < 1 ||
            !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            throw new InvalidOperationException("Dizang requires an actual enabled owner trigger.");
        if (operation == SkillProgramEffectOp.DamageHellBufoAdjacent)
        {
            if (frame.HellBufo is not null || frame.WindowContext is not
                { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context ||
                frame.OwnerSeat != _currentSeat || context.SourceSeat != frame.OwnerSeat)
                throw new InvalidOperationException("Bufo requires its exact actual own turn start.");
            var targets = Enumerable.Range(1, _playerCount - 1).Select(offset => TableStepSeat(frame.OwnerSeat, offset))
                .Where(seat => _players[seat].IsAlive && GetCombatDistance(frame.OwnerSeat, seat) == 1).ToArray();
            ReplaceRuntimeTop(frame = frame with { HellBufo = new(HellDizangSource(frame), frame.GameplayHash,
                frame.InstructionIndex, _turnNumber, 0) { Targets = targets } });
            for (var index = 0; index < targets.Length; index++)
                AdvanceEventRulesAndQueueFact(new HellBufoTargetFrozenEvent(frame.Id, frame.OwnerSeat,
                    targets[index], index, targets.Length));
            return ResumeHellBufo(frame.Id) ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
        }
        if (operation != SkillProgramEffectOp.ChooseHellDayuanJudgment || frame.HellDayuan is not null ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentBeforeResult, Judgment: { } judgment } window ||
            ActiveJudgment is not { } pending || pending.Id != judgment.JudgmentFrameId ||
            GetJudgmentCard(pending)?.Id != judgment.CardId)
            throw new InvalidOperationException("Dayuan requires the exact final native judgment and unchanged physical card.");
        ReplaceRuntimeTop(frame = frame with { HellDayuan = new(HellDizangSource(frame), frame.GameplayHash,
            frame.InstructionIndex, window.ParentFrameId, pending.Id, judgment.CardId) });
        PublishHellDayuanChoice(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 40080)]
    private bool ResumeHellBufo(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HellBufo: { } receipt } frame || frame.Id != frameId)
            return false;
        AssertHellDizangFrame(frame);
        if (frame.AttackAttempt is not null) return false;
        if (_winner != Winner.None)
        {
            ReplaceRuntimeTop(frame with { HellBufo = null });
            FinishProgramSkill(GetActiveProgramFrame(frameId), false);
            return true;
        }
        while (receipt.NextTargetIndex < receipt.Targets.Count)
        {
            var target = receipt.Targets[receipt.NextTargetIndex];
            receipt = receipt with { NextTargetIndex = receipt.NextTargetIndex + 1 };
            ReplaceRuntimeTop(frame = frame with { HellBufo = receipt });
            if (!_players[target].IsAlive) continue;
            BeginProgramSkillDamage(frame, target, 1, nature: DamageNature.Fire);
            return true;
        }
        ReplaceRuntimeTop(frame with { HellBufo = null });
        return false;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40080)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40080)]
    private bool RetainHellBufoChildren(ProgramSkillFrame frame) => frame.HellBufo is not null;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 40080)]
    private void AssertHellDizangFrame(ProgramSkillFrame frame)
    {
        if (frame.HellBufo is { } bufo &&
            (bufo.Source != HellDizangSource(frame) || bufo.GameplayHash != frame.GameplayHash ||
             bufo.InstructionIndex != frame.InstructionIndex || bufo.ActualTurnNumber != _turnNumber ||
             bufo.Targets.Any(seat => !IsValidPlayerSeat(seat) || seat == frame.OwnerSeat) ||
             bufo.Targets.Count != bufo.Targets.Distinct().Count() ||
             bufo.Targets is not System.Collections.IList { IsReadOnly: true } ||
             bufo.NextTargetIndex < 0 || bufo.NextTargetIndex > bufo.Targets.Count ||
             CompleteProgramEventHistory().OfType<HellBufoTargetFrozenEvent>().Count(e => e.ProgramFrameId == frame.Id) != bufo.Targets.Count ||
             bufo.Targets.Where((seat, index) => CompleteProgramEventHistory().OfType<HellBufoTargetFrozenEvent>().Count(e =>
                 e.ProgramFrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat && e.TargetSeat == seat &&
                 e.Index == index && e.Total == bufo.Targets.Count) != 1).Any()))
            throw new InvalidOperationException("Bufo lost its frozen actual-distance target list or paid child cursor.");
        if (frame.HellDayuan is { } dayuan &&
            (dayuan.Source != HellDizangSource(frame) || dayuan.GameplayHash != frame.GameplayHash ||
             dayuan.InstructionIndex != frame.InstructionIndex || dayuan.SelectedSuit is Suit.None ||
             frame.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentBeforeResult, Judgment: { } judgment } context ||
             dayuan.WindowFrameId != context.ParentFrameId || dayuan.JudgmentFrameId != judgment.JudgmentFrameId ||
             dayuan.CardId != judgment.CardId || ActiveJudgment is not { } pending ||
             pending.Id != dayuan.JudgmentFrameId || GetJudgmentCard(pending)?.Id != dayuan.CardId))
            throw new InvalidOperationException("Dayuan lost its exact suspended native judgment or chosen suit.");
    }

    private IReadOnlyList<PromptChoice> HellDayuanChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.HellDayuan ?? throw new InvalidOperationException("Dayuan choice has no owning frame receipt.");
        return receipt.SelectedSuit is null
            ? Array.AsReadOnly(new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }.Select(suit =>
                new PromptChoice(new($"hell-dayuan.{frame.Id}.suit.{suit}"), suit switch
                { Suit.Spade => "黑桃", Suit.Heart => "红桃", Suit.Club => "梅花", _ => "方块" }, [], [],
                    new Dictionary<string, string> { ["program-action"] = "hell-dayuan", ["suit"] = suit.ToString() })).ToArray())
            : Array.AsReadOnly(Enumerable.Range(1, 13).Select(rank =>
                new PromptChoice(new($"hell-dayuan.{frame.Id}.rank.{rank}"), rank switch
                { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => rank.ToString(CultureInfo.InvariantCulture) }, [], [],
                    new Dictionary<string, string> { ["program-action"] = "hell-dayuan", ["rank"] = rank.ToString(CultureInfo.InvariantCulture) })).ToArray());
    }

    private void PublishHellDayuanChoice(ProgramSkillFrame frame)
    {
        AssertHellDizangFrame(frame);
        ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            frame.HellDayuan!.SelectedSuit is null ? "大愿：指定实际判定牌的花色。" : "大愿：指定实际判定牌的点数。",
            [], [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = false,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description),
            Choices = HellDayuanChoices(frame)
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("hell-dayuan")]
    private void AnswerHellDayuan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HellDayuan: { } receipt } frame ||
            _pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual([choice], HellDayuanChoices(frame).Where(candidate => candidate.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Dayuan lost its exact native chooser and current answer domain.");
        AssertHellDizangFrame(frame);
        ClearPendingDecision();
        if (receipt.SelectedSuit is null)
        {
            ReplaceRuntimeTop(frame = frame with { HellDayuan = receipt with { SelectedSuit = Enum.Parse<Suit>(choice.Parameters["suit"]) } });
            PublishHellDayuanChoice(frame);
            return;
        }
        var pending = ActiveJudgment!;
        var overridden = new HellDayuanJudgmentOverride(frame.Id, pending.Id, receipt.Source, receipt.GameplayHash,
            receipt.CardId, receipt.SelectedSuit.Value, int.Parse(choice.Parameters["rank"], CultureInfo.InvariantCulture));
        ReplaceJudgmentFrame(pending with { HellDayuanOverride = overridden, Suit = overridden.Suit });
        AdvanceEventRulesAndQueueFact(new HellDayuanJudgmentOverriddenEvent(overridden));
        ReplaceRuntimeTop(frame = frame with { HellDayuan = null });
        AdvanceRuntimeProgram(frame.Id);
    }

    [ProgramAiChoice(SkillProgramEffectOp.ChooseHellDayuanJudgment)]
    private PromptChoice HellDayuanAiChoice(ProgramSkillFrame frame)
    {
        var choices = HellDayuanChoices(frame);
        if (frame.HellDayuan!.SelectedSuit is not null)
            return choices.Single(choice => choice.Parameters.GetValueOrDefault("rank") == "5");
        var judgment = frame.WindowContext!.Judgment!;
        var ally = judgment.SubjectSeat == frame.OwnerSeat || _modeDefinition.ModeKind == ContentModeKind.Team &&
            _players[judgment.SubjectSeat].TeamId == _players[frame.OwnerSeat].TeamId;
        var desired = ally ? ActiveJudgment!.Continuation == JudgmentContinuationKind.SupplyShortage ? Suit.Club : Suit.Heart : Suit.Spade;
        return choices.Single(choice => choice.Parameters.GetValueOrDefault("suit") == desired.ToString());
    }

    private HellDayuanJudgmentOverride? EffectiveHellDayuanOverride(JudgmentFrame frame)
    {
        if (frame.HellDayuanOverride is not { } receipt) return null;
        if (receipt.JudgmentFrameId != frame.Id || receipt.CardId != frame.CardId || receipt.Rank is < 1 or > 13 ||
            receipt.Suit is not (Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond) ||
            receipt.GameplayHash != _contentRegistry.GetSkill(receipt.Source.SkillId).Program?.GameplayHash ||
            CompleteProgramEventHistory().OfType<HellDayuanJudgmentOverriddenEvent>().Count(e => e.Receipt == receipt) != 1)
            throw new InvalidOperationException("Dayuan's effective judgment lost its unchanged original entity or exact answer receipt.");
        return frame.ZhenyiOverride is { } other && other.ProgramFrameId > receipt.ProgramFrameId ? null : receipt;
    }
    private Suit EffectiveHellDayuanSuit(JudgmentFrame frame, Suit fallback) => EffectiveHellDayuanOverride(frame)?.Suit ?? fallback;
    private int EffectiveHellDayuanRank(JudgmentFrame frame, int fallback) => EffectiveHellDayuanOverride(frame)?.Rank ?? fallback;

    private bool IsExactHellDayuanBeforeResultProgramChild(ProgramJudgmentTriggerWindowFrame window, ProgramSkillFrame child)
    {
        if (window.ZhenyiBeforeResult != true || !window.Activated || window.CandidateIndex < 0 ||
            window.CandidateIndex >= window.Candidates.Count || child.WindowContext is not
                { Window: SkillProgramTriggerWindow.JudgmentBeforeResult, Judgment: { } result } context ||
            context.ParentFrameId != window.Id || result != window.Judgment || ActiveJudgment is not { } pending ||
            pending.Id != window.ParentFrameId || pending.Id != result.JudgmentFrameId ||
            pending.ZhenyiBeforeResultCompleted == true || GetJudgmentCard(pending)?.Id != result.CardId)
            return false;
        var candidate = window.Candidates[window.CandidateIndex];
        return candidate.OwnerSeat == child.OwnerSeat && candidate.SkillId == child.SkillId &&
            candidate.TriggerId == child.TriggerId && candidate.SkillInstanceId == child.SkillInstanceId &&
            candidate.GameplayHash == child.GameplayHash && GetProgramTrigger(child).Effects is
                [{ Op: SkillProgramEffectOp.ChooseHellDayuanJudgment }];
    }

    private bool CanHellDitingMountRecast(CharacterState actor, Card card) =>
        EnabledContentSkillIds(actor).Contains("hell:diting", StringComparer.Ordinal) &&
        EquipmentCatalog.IsEquipment(card.Kind) && EquipmentCatalog.Get(card.Kind).Slot is
            EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse;

    // The existing native recast owns the physical payment. This optional branch
    // adds the same request/typed-return boundary used by ordinary program draws.
    private bool TryContinueEquipmentRecastNativeDraw(EquipmentRecastFrame supplied)
    {
        if (!HasNativeDrawAttemptCapability) return false;
        if (_resolutionStack.LastOrDefault() is not EquipmentRecastFrame frame || frame.Id != supplied.Id || frame.DrawApplied)
            throw new InvalidOperationException("An equipment recast draw lost its exact already-paid native owner.");
        if (frame.NativeDraw is null)
        {
            ReplaceRuntimeTop(frame = frame with
            {
                NativeDraw = new NativeDrawProducerReceipt(0, 1, null, SkillProgramCardSetVisibility.Private, CardMoveReasons.RecastDraw)
                { Recipients = [frame.ActorSeat], ReturnKind = NativeDrawAttemptReturnKind.EquipmentRecast }
            });
            if (TryBeginNativeDrawAttempt(frame)) return true;
        }
        var paid = ((EquipmentRecastFrame)_resolutionStack.Last()).NativeDraw!;
        if (paid.ActiveAttemptId is not null && !paid.AttemptReturned)
            throw new InvalidOperationException("An equipment recast cannot draw before its exact request child returns.");
        var actor = _players[frame.ActorSeat];
        var drawn = paid.Replacement is null && actor.IsAlive && _winner == Winner.None
            ? DrawCards(actor, 1, false, CardMoveReasons.RecastDraw) : Array.Empty<int>();
        frame = (EquipmentRecastFrame)_resolutionStack.Last();
        ReplaceRuntimeTop(frame = frame with { NativeDraw = null, DrawApplied = true, DrawCount = drawn.Count });
        if (_aiBrains.TryGetValue(actor.Seat, out var brain)) brain.ObserveRecast(_turnNumber, frame.CardId);
        AdvanceEventRulesAndQueueFact(new CardRecastEvent(actor.Seat, frame.CardId, frame.EffectiveKind, drawn.Count));
        CompleteNativeRecastInvocation(frame.NativeRecastInvocation, drawn.Count);
        if (frame.NativeRecastInvocation is not null)
            ReplaceRuntimeTop(((EquipmentRecastFrame)_resolutionStack.Last()) with { NativeRecastInvocation = null });
        AddLog("Recast", $"{actor.Name} 重铸【{CardCatalog.Get(frame.EffectiveKind).DisplayName}】，摸 {drawn.Count} 张牌。", actor.Seat);
        ContinueEquipmentRecast(frame.Id);
        return true;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 40081)]
    private void AssertEquipmentRecastNativeDraw()
    {
        foreach (var frame in _resolutionStack.OfType<EquipmentRecastFrame>().Where(frame => frame.NativeDraw is not null))
        {
            var paid = frame.NativeDraw!;
            if (!HasNativeDrawAttemptCapability || frame.DrawApplied || paid.ReturnKind != NativeDrawAttemptReturnKind.EquipmentRecast ||
                paid.InstructionIndex != 0 || paid.RequestedCount != 1 || paid.ResultBind is not null ||
                paid.Reason != CardMoveReasons.RecastDraw || paid.RecipientIndex != 0 || !paid.Recipients.SequenceEqual([frame.ActorSeat]) ||
                paid.Operation is not null || paid.RecipientApplied ||
                paid.ActiveAttemptId is null && (paid.AttemptReturned || paid.Replacement is not null) ||
                !_cardMovements.Any(move => move.CardId == frame.CardId && move.Reason == CardMoveReasons.RecastDiscard &&
                    move.From.OwnerSeat == frame.ActorSeat && move.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                    move.To == CardLocation.DiscardPile))
                throw new InvalidOperationException("A native equipment recast lost its sole original payment or exact one-card draw return.");
        }
    }
}
