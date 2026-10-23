using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IOlShenZhenJiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class OlShenfuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOlShenZhenJiShenfu;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new OlShenfuHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class OlShenfuHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOlShenZhenJiShenfu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IOlShenZhenJiHost>().Resolve(f);
}
public enum OlShenfuStage { Choosing, HandCard, Drawing, Damaging, Children }
public sealed record OlShenfuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex, int ActualTurn,
    bool Odd, OlShenfuStage Stage = OlShenfuStage.Choosing, int Ordinal = 0, int TargetSeat = -1,
    string? Mode = null, long MovementBefore = 0, long MovementAfter = 0, bool KilledByDamage = false,
    NativeDrawOutcome? DrawOutcome = null)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OlShenfuReceipt? OlShenfu { get; init; } }
public sealed record OlShenfuStartedEvent(long FrameId, CardConversionSource Source, int ActualTurn, bool Odd) : IGameEvent;
public sealed record OlShenfuTargetSelectedEvent(long FrameId, CardConversionSource Source, int ActualTurn, int Ordinal, int TargetSeat, string Mode) : IGameEvent;
public sealed record OlShenfuDiscardedEvent(long FrameId, int Ordinal, int TargetSeat, long Before, long After) : IGameEvent;
public sealed record OlShenfuDrawReturnedEvent(long FrameId, int Ordinal, int TargetSeat, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record OlShenfuDeathConfirmedEvent(long FrameId, int Ordinal, int TargetSeat, long DamageFrameId, long DyingFrameId) : IGameEvent;
public sealed record OlShenfuFlowResolvedEvent(long FrameId, int Ordinal, int TargetSeat, string Mode, bool MayRepeat) : IGameEvent;
internal static class OlShenZhenJiComposition
{
    [ProgramCatalogValidation(40525)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveOlShenZhenJiShenfu)) return;
        if (p.Id != "ol:shenfu" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.Triggers is not [{ Window: SkillProgramTriggerWindow.TurnEnding, TurnOwnerScope: SkillProgramTurnOwnerScope.Own,
                Subject: SkillProgramTriggerSubject.Owner, Optional: true, IsSkillInvocation: true,
                Effects: [{ Op: SkillProgramEffectOp.ResolveOlShenZhenJiShenfu, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }])
            throw new InvalidOperationException(path + ": OL event Shenfu requires its own actual turn ending and one accepted repeating operation.");
    }
}
