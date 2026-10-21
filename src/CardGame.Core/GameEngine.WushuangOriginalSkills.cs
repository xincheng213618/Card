using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record WushuangOriginalSkillReceipt(SkillProgramEffectOp Operation, int InstructionIndex,
    int ActualTurn, long? WindowId, string Stage, int Target = -1, int Card = 0, int Index = 0,
    int Count = 0, int Mask = 0, int Correct = -1, int DrawOrdinal = 0, int DrawRecipient = -1,
    int DrawRequested = 0, string? NextStage = null, NativeDrawOutcome? Draw = null)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _top = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Top { get => _top; init => _top = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangOriginalSkillReceipt? WushuangOriginalSkill { get; init; }
}
public sealed record WushuangOriginalSkillIssuedEvent(long FrameId, string SkillId, int OwnerSeat,
    SkillProgramEffectOp Operation, int InstructionIndex, int ActualTurn, long? WindowId) : IGameEvent;
public sealed record WushuangOriginalSkillFinishedEvent(long FrameId, bool Completed) : IGameEvent;
public sealed record WushuangOriginalSkillDrawEvent(long FrameId, int Ordinal, int Recipient, int Requested,
    int Actual, long Before, long After, long? Attempt, bool Replaced) : IGameEvent;
public sealed record WushuangTairanHealedEvent(long FrameId, int Owner, string Instance, int ActualTurn, int Recovered) : IGameEvent;
public sealed record WushuangTairanDrawnEvent(long FrameId, int Owner, string Instance, int ActualTurn, IReadOnlyList<int> CardIds) : IGameEvent
{
    private readonly IReadOnlyList<int> _ids = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WushuangTairanSettledEvent(long ProducerId, int Owner, long SettlingFrame) : IGameEvent;
public sealed record WushuangYashiSuppressedEvent(long FrameId, int Owner, int Target, int ActualTurn) : IGameEvent;
public sealed record WushuangYashiExpiredEvent(long ProducerId, int Target) : IGameEvent;
public sealed record WushuangLingrenGuessedEvent(long FrameId, int Owner, int Target, int ActualTurn,
    long UseId, long ActionId, int Correct) : IGameEvent;
public sealed record WushuangChoufaRewrittenEvent(long FrameId, int Owner, int Target, int ActualTurn,
    int MovementSequence, CardUseEffectSource Source, IReadOnlyList<int> CardIds) : IGameEvent
{
    private readonly IReadOnlyList<int> _ids = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WushuangChoufaExpiredEvent(long ProducerId, int Target) : IGameEvent;
public sealed record WushuangCiweiNullifiedEvent(long FrameId, long UseId, long WindowId, int Owner,
    long ActionId, int Actor, CardKind Kind, string SkillId, string SkillInstanceId, string GameplayHash,
    int InstructionIndex, int PaidCard, int PaidMovement) : IGameEvent;
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangCiweiNullifiedEvent? WushuangCiweiNullification { get; init; }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class WushuangOriginalSkillHost(GameEngine engine) : IWushuangOriginalSkillHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.BeginWushuangOriginalSkill(frame, operation); }

    private bool WushuangOriginalEnabled(CharacterState owner, string id) => owner.IsAlive &&
        _contentRegistry.Skills.ContainsKey(id) && EnabledSkillPrograms(owner).Any(p => p.Id == id);

    private SkillProgramStepOutcome BeginWushuangOriginalSkill(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WushuangOriginalSkill is not null || GetPausedPrivateOfferEffect(f)?.Op != operation)
            throw new InvalidOperationException("An original Wushuang skill requires its exact unpaid instruction.");
        var r = new WushuangOriginalSkillReceipt(operation, f.InstructionIndex, _turnNumber,
            f.WindowContext?.ParentFrameId, "begin");
        ReplaceRuntimeTop(f = f with { WushuangOriginalSkill = r });
        AdvanceEventRulesAndQueueFact(new WushuangOriginalSkillIssuedEvent(f.Id, f.SkillId, f.OwnerSeat,
            operation, f.InstructionIndex, _turnNumber, r.WindowId));
        RunWushuangOriginalSkill(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private ProgramSkillFrame UpdateWushuangOriginal(ProgramSkillFrame f, WushuangOriginalSkillReceipt r)
    { ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { WushuangOriginalSkill = r }); return f; }
    private ProgramSkillFrame WushuangOriginalStage(ProgramSkillFrame f, string stage) => UpdateWushuangOriginal(f, f.WushuangOriginalSkill! with { Stage = stage });
    private bool DrainWushuangOriginal(long id) =>
        TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);

    [ProgramContinuation(ProgramContinuationKind.Resume, 61010)]
    private bool RunWushuangOriginalSkill(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WushuangOriginalSkill: { } r } f || f.Id != id) return false;
        AssertWushuangOriginalSkill(f);
        if (r.Stage == "draw-attempt") return true;
        if (DrainWushuangOriginal(id)) return true;
        f = GetActiveProgramFrame(id); r = f.WushuangOriginalSkill!;
        if (r.Stage == "finish") { FinishWushuangOriginal(f); return true; }
        switch (r.Operation)
        {
            case SkillProgramEffectOp.ResolveWushuangOriginalYiji: ContinueWushuangYiji(f); break;
            case SkillProgramEffectOp.ResolveWushuangYashi: ContinueWushuangYashi(f); break;
            case SkillProgramEffectOp.ResolveWushuangGuanxu: ContinueWushuangGuanxu(f); break;
            case SkillProgramEffectOp.ResolveWushuangChoufa: ContinueWushuangChoufa(f); break;
            case SkillProgramEffectOp.ResolveWushuangShenfu: ContinueWushuangShenfu(f); break;
            case SkillProgramEffectOp.ResolveWushuangLingren: ContinueWushuangLingren(f); break;
            case SkillProgramEffectOp.ResolveWushuangTairan: ContinueWushuangTairan(f); break;
            case SkillProgramEffectOp.SettleWushuangTairan: ContinueWushuangTairanSettlement(f); break;
            case SkillProgramEffectOp.NullifyWushuangSecondUse: NullifyWushuangCiweiUse(f); break;
            case SkillProgramEffectOp.ResolveWushuangOriginalJieming: ContinueWushuangJieming(f); break;
            case SkillProgramEffectOp.PayAndDrawWushuangJinzhi: ContinueWushuangJinzhi(f); break;
            case SkillProgramEffectOp.SkipWushuangFenliPhase: SkipWushuangFenliPhase(f); break;
            case SkillProgramEffectOp.RestrictWushuangQianxiJudgmentColor: RestrictWushuangQianxiJudgmentColor(f); break;
            default: throw new InvalidOperationException("Unknown official Wushuang skill operation.");
        }
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 61010)]
    private bool ReturnWushuangOriginalMovement(ProgramSkillFrame f) => f.WushuangOriginalSkill is not null && RunWushuangOriginalSkill(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 61010)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 61010)]
    private bool ContinuePaidWushuangOriginal(ProgramSkillFrame f) => f.WushuangOriginalSkill is { Stage: not "begin" };

    private void FinishWushuangOriginal(ProgramSkillFrame f, bool completed = true)
    {
        AdvanceEventRulesAndQueueFact(new WushuangOriginalSkillFinishedEvent(f.Id, completed));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { WushuangOriginalSkill = null });
        AdvanceRuntimeProgram(f.Id);
    }
    private void DrawWushuangOriginal(ProgramSkillFrame f, int recipient, int count, string next)
    {
        f = UpdateWushuangOriginal(f, f.WushuangOriginalSkill! with { Stage = "draw-attempt",
            DrawOrdinal = f.WushuangOriginalSkill.DrawOrdinal + 1, DrawRecipient = recipient,
            DrawRequested = Math.Max(0, count), NextStage = next, Draw = null });
        BeginOperationProgramDraw(f, recipient, Math.Max(0, count), new("skill-program." + f.SkillId + ".draw"));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWushuangShenfu, SkillProgramEffectOp.ResolveWushuangLingren,
        SkillProgramEffectOp.ResolveWushuangTairan, SkillProgramEffectOp.ResolveWushuangOriginalJieming, SkillProgramEffectOp.PayAndDrawWushuangJinzhi)]
    private void ReturnWushuangOriginalDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        var r = f.WushuangOriginalSkill ?? throw new InvalidOperationException("The official skill draw lost its producer.");
        if (r.Stage != "draw-attempt" || outcome.RecipientSeat != r.DrawRecipient || outcome.RequestedCount != r.DrawRequested ||
            ProgramEventHistory<WushuangOriginalSkillDrawEvent>().Any(e => e.FrameId == f.Id && e.Ordinal == r.DrawOrdinal))
            throw new InvalidOperationException("The official skill draw changed its paid recipient or requested count.");
        AdvanceEventRulesAndQueueFact(new WushuangOriginalSkillDrawEvent(f.Id, r.DrawOrdinal, outcome.RecipientSeat,
            outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter,
            outcome.AttemptFrameId, outcome.Replacement is not null));
        if (r.Operation == SkillProgramEffectOp.ResolveWushuangTairan)
            AdvanceEventRulesAndQueueFact(new WushuangTairanDrawnEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, _turnNumber, outcome.ActualDrawIds));
        UpdateWushuangOriginal(f, r with { Stage = r.NextStage!, NextStage = null, Draw = outcome });
        RunWushuangOriginalSkill(f.Id);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 61010)]
    private void AssertWushuangOriginalSkill(ProgramSkillFrame f)
    {
        if (f.WushuangOriginalSkill is not { } r) return;
        if (r.InstructionIndex != f.InstructionIndex || r.ActualTurn != _turnNumber ||
            r.WindowId != f.WindowContext?.ParentFrameId || GetPausedPrivateOfferEffect(f)?.Op != r.Operation ||
            ProgramEventHistory<WushuangOriginalSkillIssuedEvent>().Count(e => e == new WushuangOriginalSkillIssuedEvent(
                f.Id, f.SkillId, f.OwnerSeat, r.Operation, r.InstructionIndex, r.ActualTurn, r.WindowId)) != 1 ||
            r.Cards.Distinct().Count() != r.Cards.Count || r.Top.Distinct().Count() != r.Top.Count ||
            r.Stage == "draw-attempt" && (r.NextStage is null || r.Draw is not null || !IsValidPlayerSeat(r.DrawRecipient)))
            throw new InvalidOperationException("The official skill receipt lost its exact owning instruction or issuance.");
    }
    private PromptChoice WushuangOriginalChoice(ProgramSkillFrame f, string option, string label,
        IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => new(
        new($"wushuang-original.{f.Id}.{f.WushuangOriginalSkill!.Stage}.{option}.{string.Join('-', cards ?? [])}.{string.Join('-', targets ?? [])}"),
        label, cards ?? [], targets ?? [], new Dictionary<string, string>
        { ["program-action"] = "wushuang-original", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option });
    private void PublishWushuangOriginalChoice(ProgramSkillFrame f, IReadOnlyList<PromptChoice> choices)
    {
        if (choices.Count == 0) { FinishWushuangOriginal(f, false); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"【{skill.Name}】请选择。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private PromptChoice[] WushuangOtherTargets(ProgramSkillFrame f, string option, Func<CharacterState, bool>? condition = null) =>
        _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && (condition?.Invoke(p) ?? true))
            .Select(p => WushuangOriginalChoice(f, option, p.Name, targets: [p.Seat])).ToArray();

    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 61010)]
    private IEnumerable<CardSnapshot> WushuangOriginalPrivateCards(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.OwnerSeat == viewer && f.WushuangOriginalSkill is { Operation:
            SkillProgramEffectOp.ResolveWushuangOriginalYiji or SkillProgramEffectOp.ResolveWushuangGuanxu or SkillProgramEffectOp.ResolveWushuangYashi })
        .SelectMany(f => f.WushuangOriginalSkill!.Cards.Concat(f.WushuangOriginalSkill.Top)).Distinct()
        .Select(id => ToSnapshot(GetAdvancedCard(id)));
}
