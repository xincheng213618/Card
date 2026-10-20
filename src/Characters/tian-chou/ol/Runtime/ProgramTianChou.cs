using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum TianChouShandaoStage { Targets, Card, TopChildren, TrickReady, NativeUse, NativeChildren }
public sealed record TianChouShandaoTop(int Index, int OwnerSeat, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Before, long After, long Sequence)
{
    private readonly IReadOnlyList<int> _grain = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> WoodenOxGrainIds { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
}
public sealed record TianChouShandaoUseOrigin(long ProgramFrameId, int InstructionIndex, long CardUseFrameId,
    CardConversionSource Source, string GameplayHash, int ActualTurn, int PhaseInstanceId, int ActorSeat, CardKind CardKind)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> TargetSeats { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record TianChouShandaoReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurn, int PhaseInstanceId, long StartedAt, TianChouShandaoStage Stage = TianChouShandaoStage.Targets)
{
    private readonly IReadOnlyList<int> _candidates = Array.AsReadOnly(Array.Empty<int>()), _targets = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<TianChouShandaoTop> _tops = Array.AsReadOnly(Array.Empty<TianChouShandaoTop>());
    public IReadOnlyList<int> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<TianChouShandaoTop> Tops { get => _tops; init => _tops = Array.AsReadOnly(value.Select(s => s with { WoodenOxGrainIds = s.WoodenOxGrainIds }).ToArray()); }
    public int Cursor { get; init; }
    public int TrickIndex { get; init; }
    public bool TargetsFrozen { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TianChouShandaoUseOrigin? NativeUse { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TianChouShandaoReceipt? TianChouShandao { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TianChouShandaoUseOrigin? TianChouShandao { get; init; } }
public sealed record TianChouShandaoStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int PhaseInstanceId, long MovementSequence, int CandidateCount) : IGameEvent;
public sealed record TianChouShandaoCandidateEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record TianChouShandaoSelectedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record TianChouShandaoTargetsFrozenEvent(long FrameId, int Count) : IGameEvent;
public sealed record TianChouShandaoTargetOrderedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record TianChouShandaoTopPaidEvent(long FrameId, int Index, int OwnerSeat, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Before, long After, long Sequence) : IGameEvent;
public sealed record TianChouShandaoSkippedEvent(long FrameId, int Index, int OwnerSeat) : IGameEvent;
public sealed record TianChouShandaoNativeIssuedEvent(long FrameId, long UseId, CardKind Kind, int ActorSeat, int TargetCount) : IGameEvent;
public sealed record TianChouShandaoNativeTargetEvent(long FrameId, long UseId, int Index, int TargetSeat) : IGameEvent;
public sealed record TianChouShandaoNativeReturnedEvent(long FrameId, long UseId, CardKind Kind) : IGameEvent;
public sealed record TianChouShandaoTrickSkippedEvent(long FrameId, CardKind Kind) : IGameEvent;
public sealed record TianChouShandaoCompletedEvent(long FrameId, int PlacedCards, int IssuedTricks, string Reason) : IGameEvent;
internal interface ITianChouHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveShandaoTopAndGlobalTricksHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShandaoTopAndGlobalTricks;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<ITianChouHost>().Resolve(f);
}
internal sealed class ResolveShandaoTopAndGlobalTricksDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShandaoTopAndGlobalTricks;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveShandaoTopAndGlobalTricksHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
}
internal static class TianChouComposition
{
    [ProgramCatalogValidation(340)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShandaoTopAndGlobalTricks)) && !p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShandaoTopAndGlobalTricks))) return;
        if (p.Id != "ol:shandao" || p.Activations is not [var a] || p.Triggers.Count != 0 ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveShandaoTopAndGlobalTricks, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.Id != "top-and-two-tricks" || a.UsageGroup != a.Id || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 || a.TargetKind != SkillProgramTargetKind.AnyLiving ||
            a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always || a.ContinueAfterOwnerDeath ||
            a.MarkerCost is not null || a.CardCountExpression is not null || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 ||
            a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null || !a.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand }) ||
            p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Shandao requires its complete once-per-real-Play owned top movement and two native global tricks.");
    }
}
