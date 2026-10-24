using System.Text.Json.Serialization;

namespace CardGame.Core;

// The first opportunity belongs to the entire actual turn, even if every
// Chen Zhi declines. The shared discard classifier decides what "discard" is.
public sealed record MobileShiChenZhiFirstDiscardEvent(int ActualTurn, long BatchId, int MovementSequence) : IGameEvent;
public sealed record MobileShiChenZhiExtraGrantedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int IssuedTurn, int IssuedRound) : IGameEvent;
public sealed record MobileShiChenZhiExtraEnteredEvent(long GrantFrameId, int OwnerSeat, int UpcomingTurn, bool LostHp) : IGameEvent;
public sealed record MobileShiChenZhiRenxingCompletedEvent(long FrameId, CardConversionSource Source,
    int ActualTurn, long FirstDiscardBatch, string Branch, int TargetSeat) : IGameEvent;

public enum MobileShiChenZhiStage { DiscardChildren, ExtraStartChildren, ChooseEffect, ChooseTarget, ChooseCard, DrawOwnerChildren, DrawActorChildren, ForeignDiscardChildren, Finished }
public sealed record MobileShiChenZhiReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    int ActualTurn, int TurnOwner, MobileShiChenZhiStage Stage)
{
    public long FirstDiscardBatch { get; init; }
    public long GrantFrameId { get; init; }
    public int TargetSeat { get; init; } = -1;
    public string? Branch { get; init; }
    public int BeforeMovement { get; init; }
    public int AfterMovement { get; init; }
    public int CardId { get; init; }
    private readonly IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiChenZhiReceipt? MobileShiChenZhi { get; init; }
}

internal interface IMobileShiChenZhiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MobileShiChenZhiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MobileShiChenZhiHandler(op);
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveMobileShiChenZhiQuanchong ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        op == SkillProgramEffectOp.ResolveMobileShiChenZhiQuanchong
            ? [new RequireTriggerWindows([SkillProgramTriggerWindow.TurnEnding, SkillProgramTriggerWindow.TurnBeforeStart])]
            : [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}
internal sealed class ResolveMobileShiChenZhiQuanchongDescriptor() : MobileShiChenZhiDescriptor(SkillProgramEffectOp.ResolveMobileShiChenZhiQuanchong);
internal sealed class ResolveMobileShiChenZhiRenxingDescriptor() : MobileShiChenZhiDescriptor(SkillProgramEffectOp.ResolveMobileShiChenZhiRenxing);
internal sealed class MobileShiChenZhiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMobileShiChenZhiHost>().Resolve(frame, op);
}

internal static class MobileShiChenZhiComposition
{
    [ProgramCatalogValidation(47600)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveMobileShiChenZhiQuanchong or SkillProgramEffectOp.ResolveMobileShiChenZhiRenxing)) return;
        static bool Plain(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Subject == SkillProgramTriggerSubject.Owner &&
            t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var valid = program.Id == "mobile:shi-quanchong" ? program.Triggers.Count == 2 &&
            program.Triggers.Any(t => t.Id == "quanchong-ending" && t.Window == SkillProgramTriggerWindow.TurnEnding &&
                !t.Optional && t.UsageScope == SkillUsageScope.Round && t.UsageLimit == 1 && Plain(t, SkillProgramEffectOp.ResolveMobileShiChenZhiQuanchong)) &&
            program.Triggers.Any(t => t.Id == "quanchong-extra-start" && t.Window == SkillProgramTriggerWindow.TurnBeforeStart &&
                !t.Optional && t.UsageLimit is null && Plain(t, SkillProgramEffectOp.ResolveMobileShiChenZhiQuanchong)) :
            program.Id == "mobile:shi-renxing" && program.Triggers is [{ Id: "renxing-first-discard", Window: SkillProgramTriggerWindow.DiscardPileReceived,
                Optional: true, UsageScope: SkillUsageScope.Round, UsageLimit: 2, MovementDiscardOnly: true } r] && Plain(r, SkillProgramEffectOp.ResolveMobileShiChenZhiRenxing);
        if (!valid || program.Activations.Count != 0 || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Shi Chen Zhi requires once-round ending extra turn and twice-round first actual non-discard-phase discard.");
    }
}
