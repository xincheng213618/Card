using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IHulaoActivityProgramHost
{
    SkillProgramStepOutcome ExecuteHulaoActivity(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal abstract class HulaoActivityOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new HulaoActivityEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
}

internal sealed class ResolveHulaoXiuluoDescriptor : HulaoActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveHulaoXiuluo;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

internal sealed class OfferHulaoDamagePeachDescriptor : HulaoActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferHulaoDamagePeach;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

internal sealed class PreventHulaoArmoredLargeDamageDescriptor : HulaoActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventHulaoArmoredLargeDamage;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}

internal sealed class SkipHulaoTreasureJudgmentDescriptor : HulaoActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SkipHulaoTreasureJudgment;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

internal sealed class BindPublicAllyDeathDescriptor : HulaoActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindPublicAllyDeath;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied), new CreateChoiceResult(effect.ResultBind!, ["ally", "other"])];
}

internal sealed class HulaoActivityEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IHulaoActivityProgramHost>().ExecuteHulaoActivity(frame, effect);
}

public enum HulaoXiuluoStage { Offering, CostChildren, JudgmentOffering, JudgmentChildren, Complete }
public sealed record HulaoXiuluoDraft(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, long LifecycleFrameId, HulaoXiuluoStage Stage = HulaoXiuluoStage.Offering,
    int Iteration = 0, int? CostCardId = null, CardLocation? CostFrom = null, Suit PaidSuit = Suit.None, int? JudgmentCardId = null);
public enum HulaoPeachStage { Offering, Using, Children, Complete }
public sealed record HulaoPeachDraft(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, long DamageFrameId, HulaoPeachStage Stage = HulaoPeachStage.Offering,
    int? CardId = null, long? CardUseFrameId = null);
public sealed record HulaoPeachOrigin(long ProgramFrameId, HulaoPeachDraft Issuance);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HulaoXiuluoDraft? HulaoXiuluo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HulaoPeachDraft? HulaoPeach { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HulaoPeachOrigin? HulaoPeachOrigin { get; init; }
}
public sealed record HulaoXiuluoPaidEvent(long ProgramFrameId, int OwnerSeat, int Iteration, int CardId, Suit EffectiveSuit) : IGameEvent;
public sealed record HulaoXiuluoJudgmentDiscardedEvent(long ProgramFrameId, int OwnerSeat, int Iteration, int CardId) : IGameEvent;
public sealed record HulaoPeachIssuedEvent(HulaoPeachOrigin Origin) : IGameEvent;
public sealed record HulaoPeachReturnedEvent(HulaoPeachOrigin Origin) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class HulaoActivityProgramHost(GameEngine engine) : IHulaoActivityProgramHost
    {
        public SkillProgramStepOutcome ExecuteHulaoActivity(ProgramSkillFrame f, SkillProgramEffect e) => engine.ExecuteHulaoActivity(f, e);
    }
    private static CardConversionSource HulaoProgramSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HulaoProgramSourceValid(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) &&
        EnabledSkillPrograms(_players[f.OwnerSeat]).Any(p => p.Id == f.SkillId && p.GameplayHash == f.GameplayHash);
    private SkillProgramStepOutcome ExecuteHulaoActivity(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        switch (effect.Op)
        {
            case SkillProgramEffectOp.ResolveHulaoXiuluo:
                if (f.HulaoXiuluo is not null || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } xiuluo)
                    throw new InvalidOperationException("Xiuluo requires its original preparation window.");
                ReplaceRuntimeTop(f = f with { HulaoXiuluo = new(f.InstructionIndex, HulaoProgramSource(f), f.GameplayHash, _turnNumber, xiuluo.ParentFrameId) });
                if (PublishHulaoXiuluo(f)) return SkillProgramStepOutcome.AwaitChoice;
                ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { HulaoXiuluo = null });
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.OfferHulaoDamagePeach:
                if (f.HulaoPeach is not null || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } damage || damage.TargetSeat != f.OwnerSeat)
                    throw new InvalidOperationException("Shenqu Peach requires the owner's original applied-damage window.");
                if (HulaoPeaches(f).Count == 0) return SkillProgramStepOutcome.Continue;
                ReplaceRuntimeTop(f = f with { HulaoPeach = new(f.InstructionIndex, HulaoProgramSource(f), f.GameplayHash, _turnNumber, damage.ParentFrameId) });
                PublishHulaoPeach(f); return SkillProgramStepOutcome.AwaitChoice;
            case SkillProgramEffectOp.PreventHulaoArmoredLargeDamage:
                if (f.WindowContext?.Window != SkillProgramTriggerWindow.BeforeDamageApplied ||
                    _resolutionStack.ElementAtOrDefault(_resolutionStack.Count - 2) is not BeforeDamageProgramWindowFrame window ||
                    window.Id != f.WindowContext.ParentFrameId || window.TargetSeat != f.OwnerSeat)
                    throw new InvalidOperationException("Aozhan requires the live owning before-damage frame.");
                if (!window.Prevented && window.Amount > 1 && GetEquipment(_players[f.OwnerSeat]).Any(c => EquipmentCatalog.Get(c.Kind).Slot == EquipmentSlot.Armor))
                    PreventProgramCurrentDamage(f);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.BindPublicAllyDeath:
                if (f.WindowContext is not { Window: SkillProgramTriggerWindow.CharacterDied, TargetSeat: { } victim })
                    throw new InvalidOperationException("United supply requires an actual character-death window.");
                var ally = victim != f.OwnerSeat && _players[f.OwnerSeat].IsAlive && !_players[victim].IsAlive &&
                    _players[f.OwnerSeat].TeamId is { } team && _players[victim].TeamId == team;
                ReplaceRuntimeTop(f with { ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings
                    .Append(new ProgramChoiceResultBinding(effect.ResultBind!, ally ? "ally" : "other", f.OwnerSeat)).ToArray()) });
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.SkipHulaoTreasureJudgment:
                return GetEquipment(_players[f.OwnerSeat]).Any(c => EquipmentCatalog.Get(c.Kind).Slot == EquipmentSlot.Treasure)
                    ? SkipProgramTurnPhases(f, [SkillProgramTurnPhase.Judgment]) : SkillProgramStepOutcome.Continue;
            default: throw new InvalidOperationException("Unsupported Hulao activity operation.");
        }
    }

    private IReadOnlyList<PromptChoice> HulaoXiuluoChoices(ProgramSkillFrame f)
    {
        var choices = new List<PromptChoice>(); var owner = _players[f.OwnerSeat];
        if (HulaoProgramSourceValid(f))
            foreach (var card in GetHand(owner).Concat(GetEquipment(owner)).OrderBy(c => c.Id))
            {
                var from = FindOwnedCardLocation(owner, card);
                if (IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, card, from, OwnedCardMoveIntent.Discard)) continue;
                var suit = EffectiveSuit(owner, card);
                if (GetJudgment(owner).Any(j => j.Suit == suit))
                    choices.Add(new(new($"hulao-xiuluo.{f.Id}.{card.Id}"),
                        $"弃置【{card.DisplayName}】，然后选择弃置一张同花色判定牌。", [card.Id], [],
                        new Dictionary<string, string> { ["program-action"] = "hulao-xiuluo" }));
            }
        return choices;
    }
    private bool PublishHulaoXiuluo(ProgramSkillFrame f)
    {
        var choices = HulaoXiuluoChoices(f);
        if (choices.Count == 0)
        { ReplaceRuntimeTop(f with { HulaoXiuluo = f.HulaoXiuluo! with { Stage = HulaoXiuluoStage.Complete } }); return false; }
        PublishHulaoPrompt(f, "弃置一张牌并移除一张同花色判定牌，或结束修罗。", choices.Append(
            new(new($"hulao-xiuluo.{f.Id}.stop"), "结束修罗", [], [], new Dictionary<string, string> { ["program-action"] = "hulao-xiuluo" })).ToArray());
        return true;
    }
    [ProgramAnswer("hulao-xiuluo")]
    private void ResolveHulaoXiuluoChoice(PromptChoice answer)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertHulaoProgram(f); var r = f.HulaoXiuluo!;
        if (r.Stage != HulaoXiuluoStage.Offering || _pendingDecision?.PlayerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Xiuluo lost its exact preparation choice.");
        ClearPendingDecision();
        if (answer.Id.Value == $"hulao-xiuluo.{f.Id}.stop")
        { ReplaceRuntimeTop(f with { HulaoXiuluo = r with { Stage = HulaoXiuluoStage.Complete } }); AdvanceRuntimeProgram(f.Id); return; }
        var choice = HulaoXiuluoChoices(f).Single(c => c.Id == answer.Id);
        var card = GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat])).Single(c => c.Id == choice.Cards.Single());
        var from = FindOwnedCardLocation(_players[f.OwnerSeat], card); var suit = EffectiveSuit(_players[f.OwnerSeat], card);
        r = r with { Stage = HulaoXiuluoStage.CostChildren, Iteration = r.Iteration + 1, CostCardId = card.Id, CostFrom = from, PaidSuit = suit, JudgmentCardId = null };
        ReplaceRuntimeTop(f with { HulaoXiuluo = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveLogicalDiscard(f.OwnerSeat, f.OwnerSeat, card, from, new("program.hulao.xiuluo.cost"), parentFrameId: f.Id);
        AdvanceEventRulesAndQueueFact(new HulaoXiuluoPaidEvent(f.Id, f.OwnerSeat, r.Iteration, card.Id, suit));
        AdvanceRuntimeProgram(f.Id);
    }

    private IReadOnlyList<PromptChoice> HulaoXiuluoJudgmentChoices(ProgramSkillFrame f) =>
        !_players[f.OwnerSeat].IsAlive ? [] : GetJudgment(_players[f.OwnerSeat]).Where(c => c.Suit == f.HulaoXiuluo!.PaidSuit)
            .OrderBy(c => c.Id).Select(c => new PromptChoice(new($"hulao-xiuluo-judgment.{f.Id}.{c.Id}"),
                $"弃置判定区内的【{c.DisplayName}】。", [c.Id], [],
                new Dictionary<string, string> { ["program-action"] = "hulao-xiuluo-judgment" })).ToArray();
    private bool PublishHulaoXiuluoJudgment(ProgramSkillFrame f)
    {
        var choices = HulaoXiuluoJudgmentChoices(f);
        if (choices.Count == 0) return false;
        PublishHulaoPrompt(f, "修罗的弃牌效果已结算：选择一张现存的同花色判定牌弃置。", choices); return true;
    }
    [ProgramAnswer("hulao-xiuluo-judgment")]
    private void ResolveHulaoXiuluoJudgmentChoice(PromptChoice answer)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertHulaoProgram(f); var r = f.HulaoXiuluo!;
        if (r.Stage != HulaoXiuluoStage.JudgmentOffering || _pendingDecision?.PlayerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Xiuluo lost the post-payment judgment choice.");
        var choice = HulaoXiuluoJudgmentChoices(f).Single(c => c.Id == answer.Id);
        var card = GetJudgment(_players[f.OwnerSeat]).Single(c => c.Id == choice.Cards.Single()); ClearPendingDecision();
        ReplaceRuntimeTop(f with { HulaoXiuluo = r with { Stage = HulaoXiuluoStage.JudgmentChildren, JudgmentCardId = card.Id },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveLogicalDiscard(f.OwnerSeat, f.OwnerSeat, card, CardLocation.Judgment(f.OwnerSeat), new("program.hulao.xiuluo.judgment"), parentFrameId: f.Id);
        AdvanceEventRulesAndQueueFact(new HulaoXiuluoJudgmentDiscardedEvent(f.Id, f.OwnerSeat, r.Iteration, card.Id));
        AdvanceRuntimeProgram(f.Id);
    }

    private IReadOnlyList<Card> HulaoPeaches(ProgramSkillFrame f)
    {
        var owner = _players[f.OwnerSeat];
        if (!HulaoProgramSourceValid(f) || !owner.HasHpValue || owner.Hp >= owner.MaxHp ||
            IsCardUseForbidden(f.OwnerSeat, CardKind.Peach, CardActionType.Use) ||
            IsSelfTargetForbiddenAction(owner, CardKind.Peach, [f.OwnerSeat]) ||
            IsDirectedCardTargetProhibited(f.OwnerSeat, f.OwnerSeat, CardKind.Peach)) return [];
        return GetHand(owner).Where(c => c.Kind == CardKind.Peach && !IsTurnHandCardRestricted(owner, c) &&
            !IsTurnPhysicalUseForbidden(f.OwnerSeat, [c.Id]) &&
            !IsCardTargetProhibited(owner, CardKind.Peach, EffectiveSuit(owner, c), SuitColor(EffectiveSuit(owner, c))))
            .OrderBy(c => c.Id).ToArray();
    }
    private IReadOnlyList<PromptChoice> HulaoPeachChoices(ProgramSkillFrame f) => HulaoPeaches(f).Select(card => new PromptChoice(
        new($"hulao-peach.{f.Id}.{card.Id}"), "使用一张【桃】。", [card.Id], [f.OwnerSeat],
        new Dictionary<string, string> { ["program-action"] = "hulao-peach" })).Append(
        new(new($"hulao-peach.{f.Id}.stop"), "不使用【桃】", [], [], new Dictionary<string, string> { ["program-action"] = "hulao-peach" })).ToArray();
    private void PublishHulaoPeach(ProgramSkillFrame f) => PublishHulaoPrompt(f, "神躯：使用一张真实的【桃】，或放弃。", HulaoPeachChoices(f));
    private void PublishHulaoPrompt(ProgramSkillFrame f, string text, IReadOnlyList<PromptChoice> choices)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("hulao-peach")]
    private void ResolveHulaoPeachChoice(PromptChoice answer)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertHulaoProgram(f); var r = f.HulaoPeach!;
        if (r.Stage != HulaoPeachStage.Offering || _pendingDecision?.PlayerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Shenqu lost its exact real-Peach choice.");
        var choice = HulaoPeachChoices(f).Single(c => c.Id == answer.Id); ClearPendingDecision();
        if (choice.Cards.Count == 0)
        { ReplaceRuntimeTop(f with { HulaoPeach = r with { Stage = HulaoPeachStage.Complete } }); AdvanceRuntimeProgram(f.Id); return; }
        var card = HulaoPeaches(f).Single(c => c.Id == choice.Cards.Single()); var useId = _resolutionSequence + 1;
        r = r with { Stage = HulaoPeachStage.Using, CardId = card.Id, CardUseFrameId = useId };
        var origin = new HulaoPeachOrigin(f.Id, r); ReplaceRuntimeTop(f with { HulaoPeach = r });
        var actual = BeginCardUse(card, f.OwnerSeat, [f.OwnerSeat], playedCardKind: CardKind.Peach);
        if (actual != useId) throw new InvalidOperationException("Shenqu Peach issuance changed its reserved real-use identity.");
        var use = (CardUseFrame)_resolutionStack.Single(frame => frame.Id == useId);
        ReplaceRuntimeFrame(useId, use with { HulaoPeachOrigin = origin });
        AdvanceEventRulesAndQueueFact(new HulaoPeachIssuedEvent(origin));
        MoveCard(card, CardLocation.Hand(f.OwnerSeat), CardLocation.Processing, CardMoveReasons.Use);
        BeginSimpleCardUse(useId, new(card.Id, SimpleCardUseEffect.Recovery, 1));
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 27600)]
    private bool ResumeHulaoActivity(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.HulaoXiuluo is null && f.HulaoPeach is null) return false;
        AssertHulaoProgram(f);
        if (f.HulaoXiuluo is { } x)
        {
            if (x.Stage == HulaoXiuluoStage.Complete) { ReplaceRuntimeTop(f with { HulaoXiuluo = null }); return false; }
            if (x.Stage == HulaoXiuluoStage.Offering) { if (_pendingDecision is null && !PublishHulaoXiuluo(f)) AdvanceRuntimeProgram(id); return true; }
            if (x.Stage == HulaoXiuluoStage.JudgmentOffering) { if (_pendingDecision is null) PublishHulaoXiuluoJudgment(f); return true; }
            if (HulaoAwaitMovementChildren(id)) return true;
            f = GetActiveProgramFrame(id); x = f.HulaoXiuluo!;
            if (x.Stage == HulaoXiuluoStage.CostChildren)
            {
                ReplaceRuntimeTop(f = f with { HulaoXiuluo = x = x with { Stage = HulaoXiuluoStage.JudgmentOffering } });
                if (PublishHulaoXiuluoJudgment(f)) return true;
            }
            ReplaceRuntimeTop(f = f with { HulaoXiuluo = x with { Stage = HulaoXiuluoStage.Offering } });
            if (!PublishHulaoXiuluo(f)) AdvanceRuntimeProgram(id);
            return true;
        }
        var p = f.HulaoPeach!;
        if (p.Stage == HulaoPeachStage.Complete) { ReplaceRuntimeTop(f with { HulaoPeach = null }); return false; }
        if (p.Stage == HulaoPeachStage.Offering) { if (_pendingDecision is null) PublishHulaoPeach(f); return true; }
        if (p.Stage != HulaoPeachStage.Children) throw new InvalidOperationException("A real Shenqu Peach returned without its typed completion.");
        if (HulaoAwaitMovementChildren(id)) return true;
        ReplaceRuntimeTop(GetActiveProgramFrame(id) with { HulaoPeach = p with { Stage = HulaoPeachStage.Complete } });
        AdvanceRuntimeProgram(id); return true;
    }
    private bool HulaoAwaitMovementChildren(long id)
    {
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
        ReplaceRuntimeTop(GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27600)]
    private bool ContinuePaidHulaoAfterSkillLoss(ProgramSkillFrame f) =>
        f.HulaoXiuluo?.Stage is HulaoXiuluoStage.CostChildren or HulaoXiuluoStage.JudgmentOffering or HulaoXiuluoStage.JudgmentChildren ||
        f.HulaoPeach?.Stage is HulaoPeachStage.Using or HulaoPeachStage.Children;

    [NativeUseValidation(27601)]
    private void ValidateHulaoPeachUse(CardUseFrame use)
    {
        if (use.HulaoPeachOrigin is not { } origin) return;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id); var r = origin.Issuance;
        if (index <= 0 || _resolutionStack[index - 1] is not ProgramSkillFrame f || f.Id != origin.ProgramFrameId ||
            f.HulaoPeach != r || r.Stage != HulaoPeachStage.Using || use.Id != r.CardUseFrameId || use.CardId != r.CardId ||
            use.CardKind != CardKind.Peach || use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Peach } action ||
            action.ProviderSeat != f.OwnerSeat || action.PhysicalCards is not [var physical] || physical.CardId != r.CardId ||
            !MatchesDeclaredActualDamageUse(use, action.ActorSeat, f.OwnerSeat))
            throw new InvalidOperationException("A real Shenqu Peach lost its original physical payment and program owner.");
        AssertHulaoProgram(f);
    }
    [NativeUseReturn(27601)]
    private void ReturnHulaoPeachUse(CardUseFrame use)
    {
        if (use.HulaoPeachOrigin is not { } origin) return;
        var f = GetActiveProgramFrame(origin.ProgramFrameId); var r = origin.Issuance;
        if (f.HulaoPeach != r || use.Step != ResolutionFrameStep.Completed || use.Id != r.CardUseFrameId || use.CardId != r.CardId)
            throw new InvalidOperationException("A completed real Shenqu Peach lost its exact typed return.");
        ReplaceRuntimeTop(f with { HulaoPeach = r with { Stage = HulaoPeachStage.Children }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new HulaoPeachReturnedEvent(origin)); AdvanceRuntimeProgram(f.Id);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27600)]
    private void AssertHulaoProgram(ProgramSkillFrame f)
    {
        if (f.HulaoXiuluo is { } x && (x.Source != HulaoProgramSource(f) || x.GameplayHash != f.GameplayHash ||
            x.ActualTurnNumber != _turnNumber || x.InstructionIndex != f.InstructionIndex || f.WindowContext?.ParentFrameId != x.LifecycleFrameId ||
            f.WindowContext.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            HulaoPausedEffect(f).Op != SkillProgramEffectOp.ResolveHulaoXiuluo || x.Iteration < 0 ||
            x.Stage is HulaoXiuluoStage.CostChildren or HulaoXiuluoStage.JudgmentOffering or HulaoXiuluoStage.JudgmentChildren &&
                (x.CostCardId is null || x.CostFrom is null || x.Iteration == 0) ||
            x.Stage == HulaoXiuluoStage.JudgmentChildren && x.JudgmentCardId is null))
            throw new InvalidOperationException("Xiuluo lost its original preparation or paid discard cursor.");
        if (f.HulaoPeach is { } p && (p.Source != HulaoProgramSource(f) || p.GameplayHash != f.GameplayHash ||
            p.ActualTurnNumber != _turnNumber || p.InstructionIndex != f.InstructionIndex || f.WindowContext?.ParentFrameId != p.DamageFrameId ||
            f.WindowContext.Window != SkillProgramTriggerWindow.AfterDamageApplied || HulaoPausedEffect(f).Op != SkillProgramEffectOp.OfferHulaoDamagePeach ||
            p.Stage is HulaoPeachStage.Using or HulaoPeachStage.Children && (p.CardId is null || p.CardUseFrameId is null)))
            throw new InvalidOperationException("Shenqu lost its original applied-damage Peach cursor.");
    }
    private SkillProgramEffect HulaoPausedEffect(ProgramSkillFrame f) => ProgramInstructionResolver.Default
        .Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool HulaoActivityFrameRidesOn(ResolutionFrame child, ResolutionFrame parent)
    {
        if (child is CardUseFrame { HulaoPeachOrigin: { } origin } use && parent is ProgramSkillFrame owner)
        {
            if (origin.ProgramFrameId != owner.Id || owner.HulaoPeach != origin.Issuance) return false;
            ValidateHulaoPeachUse(use); return true;
        }
        if (child is ProgramCardTriggerWindowFrame window && parent is CardUseFrame { HulaoPeachOrigin: not null } peach)
        {
            ValidateHulaoPeachUse(peach);
            return window.ParentFrameId == peach.Id && window.Action.ActionId == peach.Action?.ActionId;
        }
        if (child is ProgramSkillFrame { WindowContext: { } context } observer && parent is ProgramCardTriggerWindowFrame cardWindow &&
            LifecycleCardUse(cardWindow.ParentFrameId) is { HulaoPeachOrigin: not null } actual)
        {
            ValidateHulaoPeachUse(actual);
            return context.ParentFrameId == cardWindow.Id && context.CardUse?.ParentCardUseFrameId == actual.Id &&
                cardWindow.Candidates.Any(candidate => candidate.OwnerSeat == observer.OwnerSeat && candidate.SkillId == observer.SkillId &&
                    candidate.SkillInstanceId == observer.SkillInstanceId && candidate.TriggerId == observer.TriggerId);
        }
        return false;
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHulaoXiuluo)]
    private PromptChoice SelectAiHulaoXiuluo(PendingDecision decision, ProgramSkillFrame f) => SelectHulaoActivityChoice(decision, f);
    [ProgramAiChoice(SkillProgramEffectOp.OfferHulaoDamagePeach)]
    private PromptChoice SelectAiHulaoPeach(PendingDecision decision, ProgramSkillFrame f) => SelectHulaoActivityChoice(decision, f);
    private PromptChoice SelectHulaoActivityChoice(PendingDecision decision, ProgramSkillFrame f) => decision.Choices
        .OrderBy(c => c.Cards.Count == 0 ? 1 : 0).ThenBy(c => c.Cards.Count == 0 ? 0 : GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[f.OwnerSeat])).First();
}
