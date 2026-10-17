using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ZenrunStage { Target, Cards, TakeChildren, VictimChoice, CompensationRequest, CompensationChildren }
public sealed record ZenrunSlot(CardZoneKind Zone, int Index, int CardId);
public sealed record ZenrunMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed record ZenrunReceipt(CardConversionSource Source, string GameplayHash,
    long WindowId, NativeDrawAttemptContext Draw, ZenrunStage Stage)
{
    public bool Paid { get; init; }
    public int SourceSeat { get; init; }
    public int Revision { get; init; }
    public int Step { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public int Choice { get; init; }
    private IReadOnlyList<ZenrunSlot> _selected = Array.AsReadOnly(Array.Empty<ZenrunSlot>());
    public IReadOnlyList<ZenrunSlot> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<ZenrunMaterial> _materials = Array.AsReadOnly(Array.Empty<ZenrunMaterial>());
    public IReadOnlyList<ZenrunMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZenrunReceipt? Zenrun { get; init; }
}
public sealed record ZenrunStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, NativeDrawAttemptContext Draw) : IGameEvent;
public sealed record ZenrunSourceSelectedEvent(long FrameId, int SourceSeat) : IGameEvent;
public sealed record ZenrunSlotSelectedEvent(long FrameId, int Revision, CardZoneKind Zone, int Index, bool Selected) : IGameEvent;
public sealed record ZenrunPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long AttemptFrameId, ActualDiscardRecoveryPhaseKey Phase, int SourceSeat, int Count) : IGameEvent;
public sealed record ZenrunMaterialCommittedEvent(long FrameId, int Index, int CardId, CardKind Kind, CardLocation From) : IGameEvent;
public sealed record ZenrunMovementIssuedEvent(long FrameId, int Step, ZenrunStage Stage, int Before, int After, int Count) : IGameEvent;
public sealed record ZenrunEntityMovedEvent(long FrameId, int Step, long BatchId, int CardId, CardKind Kind,
    CardLocation From, CardLocation To, CardMoveReason Reason, int Sequence) : IGameEvent;
public sealed record ZenrunVictimChoiceEvent(long FrameId, int VictimSeat, int Choice) : IGameEvent;
public sealed record ZenrunCompensationRequestedEvent(long FrameId, int RecipientSeat, int RequestedCount, long SequenceBefore) : IGameEvent;
public sealed record ZenrunCompensationReturnedEvent(long FrameId, long? AttemptFrameId, long SequenceBefore,
    long SequenceAfter, int ActualDrawCount, int ReplacementGainedCount) : IGameEvent;
public sealed record ZenrunCompensationEntityEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record ZenrunCompletedEvent(long FrameId, bool Paid, int Count, int Choice) : IGameEvent;

internal interface ISunHongZenrunHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveZenrunNativeDrawReplacementDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.NativeDrawStarting)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveZenrunNativeDrawReplacementHandler();
}
internal sealed class ResolveZenrunNativeDrawReplacementHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<ISunHongZenrunHost>().Resolve(frame);
}
internal static class SunHongZenrunComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "ol:zenrun" || p.Activations.Count != 0 || p.Triggers is not [var t] || t.Id != "replace-native-draw" ||
            t.Window != SkillProgramTriggerWindow.NativeDrawStarting || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional ||
            t.UsageScope is not null || t.UsageLimit is not null || t.OwnerRelation is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }] || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 || p.Contributions.Count != 0 ||
            p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Zenrun requires the real all-draw preattempt and its own exact phase budget and victim choice.");
    }
}
