using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum XianbiStage { Target, DiscardChoice, DiscardChildren, DrawRequest, DrawChildren, ObtainChildren, NextReturn }
public sealed record XianbiEntity(int CardId, CardKind Kind, CardKind EffectiveKind);
public sealed record XianbiReceipt(CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwner, long PlayPhase, XianbiStage Stage)
{
    public bool Issued { get; init; }
    public int TargetSeat { get; init; }
    public int HandBefore { get; init; }
    public int EquipmentCount { get; init; }
    public int Required { get; init; }
    public int Revision { get; init; }
    public int Cursor { get; init; }
    public int MovementStep { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public int ActualCount { get; init; }
    public int RandomIndex { get; init; }
    public uint RandomBefore { get; init; }
    public uint RandomAfter { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ChosenCard { get; init; }
    private IReadOnlyList<XianbiEntity> _hand = Array.AsReadOnly(Array.Empty<XianbiEntity>());
    public IReadOnlyList<XianbiEntity> Hand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<XianbiEntity> _primary = Array.AsReadOnly(Array.Empty<XianbiEntity>());
    public IReadOnlyList<XianbiEntity> Primary { get => _primary; init => _primary = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _eligible = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Eligible { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XianbiReceipt? Xianbi { get; init; }
}
public sealed record XianbiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TurnOwner, long PlayPhase) : IGameEvent;
public sealed record XianbiTargetFrozenEvent(long FrameId, int TargetSeat, int HandBefore, int EquipmentCount) : IGameEvent;
public sealed record XianbiHandEntityEvent(long FrameId, int Index, int CardId, CardKind Kind, CardKind EffectiveKind) : IGameEvent;
public sealed record XianbiSelectionChangedEvent(long FrameId, int Revision, int CardId, bool Selected) : IGameEvent;
public sealed record XianbiPrimaryCommittedEvent(long FrameId, int Index, int CardId, CardKind Kind, CardKind EffectiveKind) : IGameEvent;
public sealed record XianbiMovementIssuedEvent(long FrameId, int Step, XianbiStage Stage, int Cursor,
    int Before, int After, int Count) : IGameEvent;
public sealed record XianbiEntityMovedEvent(long FrameId, int Step, long BatchId, int CardId,
    CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, int Sequence) : IGameEvent;
public sealed record XianbiDrawnEntityEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record XianbiDrawRequestedEvent(long FrameId, int RecipientSeat, int RequestedCount, long SequenceBefore) : IGameEvent;
public sealed record XianbiDrawReturnedEvent(long FrameId, long? AttemptFrameId, long SequenceBefore,
    long SequenceAfter, int ActualDrawCount, int ReplacementGainedCount) : IGameEvent;
public sealed record XianbiRandomCandidateEvent(long FrameId, int Cursor, int Index, int CardId) : IGameEvent;
public sealed record XianbiRandomIssuedEvent(long FrameId, int Cursor, int OriginalCardId, int CardId,
    int Index, int Count, uint Before, uint After) : IGameEvent;
public sealed record XianbiReturnSkippedEvent(long FrameId, int Cursor, int OriginalCardId) : IGameEvent;
public sealed record XianbiCompletedEvent(long FrameId, bool Issued, int PrimaryCount, int CompletedReturns) : IGameEvent;
public sealed record ZenrunTargetBlockedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int OwnerSeat, int TargetSeat) : IGameEvent;

internal interface ISunHongXianbiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveXianbiHandAdjustmentAndRandomReturnsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveXianbiHandAdjustmentAndRandomReturnsHandler();
}
internal sealed class ResolveXianbiHandAdjustmentAndRandomReturnsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<ISunHongXianbiHost>().Resolve(frame);
}
internal static class SunHongComposition
{
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement)))
        { SunHongZenrunComposition.ValidateProgram(path, program); return; }
        if (!program.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns))) return;
        if (program.Id != "ol:xianbi" || program.Activations is not [var a] || a.Id != "adjust-and-return" ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
            a.TargetKind != SkillProgramTargetKind.AnyLiving || a.UsesPerTurn is not null || a.UsesPerPhase != 1 ||
            a.Condition.Kind != SkillProgramConditionKind.Always ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            program.Triggers.Count != 0 || program.Modifiers.Count != 0 || program.ViewAs.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0 ||
            program.HpSkillGrants.Count != 0 || program.DamageModifiers.Count != 0 || program.Contributions.Count != 0 ||
            program.EquipmentSuitSkillGrants.Count != 0 || program.LordSkillProjection || program.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Xianbi requires its complete once-per-Play native adjustment and exact random Discard return.");
    }
}
