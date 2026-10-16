using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IZhangYiProgramHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal sealed class ZhangYiHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IZhangYiProgramHost>().Resolve(frame, operation);
}
internal abstract class ZhangYiDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhangYiHandler(operation);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => operation == SkillProgramEffectOp.ResolveDianjunActualTurnEnd
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ResolveDianjunActualTurnEndDescriptor() : ZhangYiDescriptor(SkillProgramEffectOp.ResolveDianjunActualTurnEnd)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class ResolveKangruiFirstOwnTurnDamageDescriptor() : ZhangYiDescriptor(SkillProgramEffectOp.ResolveKangruiFirstOwnTurnDamage)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public enum ZhangYiStage { DianjunDamage, DianjunExtraPlay, DianjunReturned, KangruiDraw, KangruiChoice, KangruiRecovery, Finished, DianjunCleanupChildren }
public sealed record ZhangYiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentWindowId, int ActualTurnNumber, int RecipientSeat, ZhangYiStage Stage,
    long? DamageFrameId = null, int SequenceBefore = 0, int SequenceAfter = 0, int RecoveryRequested = 0, string? Choice = null,
    int CleanupBefore = 0, int CleanupAfter = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhangYiReceipt? ZhangYi { get; init; }
}
public sealed record ZhangYiActualDamageFactEvent(int ActualTurnNumber, int ActualTurnOwnerSeat, long DamageFrameId,
    long AttackFrameId, int SourceSeat, int TargetSeat, int Amount, bool SourceLess) : IGameEvent;
public sealed record ZhangYiResolutionStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long ParentWindowId, int ActualTurnNumber, int RecipientSeat, long? DamageFrameId) : IGameEvent;
public sealed record ZhangYiKangruiDrawIssuedEvent(long FrameId, int OwnerSeat, int RecipientSeat, int ActualTurnNumber, int SequenceBefore) : IGameEvent;
public sealed record ZhangYiKangruiChoiceEvent(long FrameId, CardConversionSource Source, int RecipientSeat, int ActualTurnNumber, string Choice, int RecoveryRequested) : IGameEvent;
public sealed record ZhangYiKangruiArmedEvent(long FrameId, CardConversionSource Source, int RecipientSeat, int ActualTurnNumber, bool DamageBonus) : IGameEvent;
public sealed record ZhangYiKangruiDamageConsumedEvent(long GrantFrameId, long AttackFrameId, int RecipientSeat, int TargetSeat, int ActualTurnNumber,
    bool ChainPropagation = false, int ChainCursor = 0) : IGameEvent;
public sealed record ZhangYiKangruiHandLimitSetEvent(long GrantFrameId, long DamageFrameId, int RecipientSeat, int ActualTurnNumber) : IGameEvent;
public sealed record ZhangYiChainInstanceIssuedEvent(long AttackFrameId, int ChainCursor, int RecipientSeat, int InheritedAmount) : IGameEvent;
public sealed record ZhangYiChainInstanceRebasedEvent(long AttackFrameId, int ChainCursor, int RecipientSeat, int RestoredAmount) : IGameEvent;
public sealed record ZhangYiDianjunCleanupStartedEvent(long FrameId, int OwnerSeat, int ActualTurnNumber, int SequenceBefore) : IGameEvent;
public sealed record ZhangYiDianjunCleanupIssuedEvent(long FrameId, int OwnerSeat, int ActualTurnNumber, int SequenceBefore, int SequenceAfter) : IGameEvent;

internal static class ZhangYiComposition
{
    private static bool IsZhangYi(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveDianjunActualTurnEnd or SkillProgramEffectOp.ResolveKangruiFirstOwnTurnDamage;
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Activations.Any(a => a.Effects.Any(e => IsZhangYi(e.Op))))
            throw new InvalidOperationException($"{path}: Zhang Yi's native end/first-damage operations require trigger bindings.");
        foreach (var trigger in program.Triggers.Where(t => t.Effects.Any(e => IsZhangYi(e.Op)))) ValidateTrigger(path, trigger);
    }
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        int selectedCardCount, bool initialSelectedTarget, int initialTargetSetMaximum)
    {
        if (!effects.Any(e => IsZhangYi(e.Op))) return;
        if (effects.Count != 1 || selectedCardCount != 0 || initialSelectedTarget || initialTargetSetMaximum != 0 ||
            effects[0].Target != SkillProgramEffectTarget.Owner || effects[0].Condition.Kind != SkillProgramConditionKind.Always ||
            effects[0].ActorReference is not null || effects[0].TargetReference is not null ||
            window != (effects[0].Op == SkillProgramEffectOp.ResolveDianjunActualTurnEnd ? SkillProgramTriggerWindow.AfterTurnEnded : SkillProgramTriggerWindow.AfterDamageApplied))
            throw new InvalidOperationException($"{path}: Zhang Yi requires one standalone zero-input owner operation in its exact native window.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => IsZhangYi(e.Op))) return;
        var dianjun = trigger.Effects[0].Op == SkillProgramEffectOp.ResolveDianjunActualTurnEnd;
        if (trigger.UsageLimit is not null || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            (dianjun ? trigger.Window != SkillProgramTriggerWindow.AfterTurnEnded || trigger.Subject != SkillProgramTriggerSubject.Owner ||
                trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || trigger.Optional :
                trigger.Window != SkillProgramTriggerWindow.AfterDamageApplied || trigger.Subject != SkillProgramTriggerSubject.Any ||
                trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || !trigger.Optional))
            throw new InvalidOperationException($"{path}: Zhang Yi requires its precise mandatory own actual-end or optional per-instance any-subject damage trigger.");
    }
}
