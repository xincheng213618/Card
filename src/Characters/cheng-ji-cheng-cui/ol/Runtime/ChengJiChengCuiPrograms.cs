using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record TousuiUseReceipt(long UseId, long ActionId, CardConversionSource Source,
    string GameplayHash, int ActorSeat, int ProviderSeat, int MaterialCount, bool BottomPaid = false,
    long Before = 0, long After = 0);
public sealed record TousuiUseIssuedEvent(long UseId, long ActionId, CardConversionSource Source,
    string GameplayHash, int ActorSeat, int ProviderSeat, int MaterialCount) : IGameEvent;
public sealed record TousuiMaterialFrozenEvent(long UseId, int Index, CardActionCost Cost) : IGameEvent;
public sealed record TousuiBottomPaidEvent(long UseId, long ActionId, int Count, long Before, long After) : IGameEvent;

public enum XumingStage { Choosing, Paying, NativeUse, Complete }
public sealed record XumingPromiseIssuedEvent(long PromiseId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TurnOwnerSeat, long DamageOwnerId, long DamageWindowId, int ActorSeat,
    int TargetSeat, int MaterialCount) : IGameEvent;
public sealed record XumingMaterialFrozenEvent(long PromiseId, CardConversionSource Source, int Index,
    int CardId, CardKind PrintedKind) : IGameEvent;
public sealed record XumingDamageIncreasedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long AttackOwnerId, long DamageWindowId, int Before, int After) : IGameEvent;
public sealed record XumingPromiseSettledEvent(long FrameId, long PromiseId, CardConversionSource Source,
    int ActualTurn, bool Used, long? UseId = null) : IGameEvent;
public sealed record XumingNativeUseIssuedEvent(long FrameId, long PromiseId, CardConversionSource Source,
    long UseId, long ActionId, int ActorSeat, int TargetSeat, CardKind Kind, int MaterialCount) : IGameEvent;
public sealed record XumingNativeUseReturnedEvent(long FrameId, long UseId) : IGameEvent;
public sealed record XumingProcessingMaterialClaimedEvent(long ProgramFrameId, long OriginalUseId, long UseId,
    long PromiseId, CardConversionSource Source, int CardId) : IGameEvent;
public sealed record XumingCompletedEvent(long FrameId) : IGameEvent;
public sealed record XumingUsePlan(CardKind Kind, LegalActionKind ActionKind, int? TargetCardId,
    CardKind? RequiredKind, string Description)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record XumingEndingReceipt(CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwnerSeat, long WindowId, XumingStage Stage, int PromiseIndex = 0, long? UseId = null,
    long? ActionId = null, XumingUsePlan? Plan = null, long Before = 0, long After = 0)
{
    private readonly IReadOnlyList<XumingPromiseIssuedEvent> _promises = Array.AsReadOnly(Array.Empty<XumingPromiseIssuedEvent>());
    public IReadOnlyList<XumingPromiseIssuedEvent> Promises { get => _promises; init => _promises = Array.AsReadOnly(value.ToArray()); }
}
public sealed record XumingUseOrigin(long ProgramFrameId, long PromiseId, CardConversionSource Source,
    string GameplayHash, long UseId, long ActionId, int ActorSeat, int TargetSeat, CardKind Kind);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TousuiUseReceipt? Tousui { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XumingUseOrigin? XumingOrigin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TousuiRequestReceipt? TousuiRequest { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XumingEndingReceipt? XumingEnding { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TousuiRequestReceipt? TousuiRequest { get; init; }
}
internal interface ITousuiSelectionQuery : IProgramSelectionQuery
{ IReadOnlyList<Card> Cards(int owner); }
[ProgramSelectionModule(ProgramSelectionStage.Sources, 5, SkillProgramEffectOp.UseSelectedCardsAs)]
internal sealed class TousuiSelectionSource : IProgramSelectionSourceProvider
{
    public bool TryBuild(ProgramSelectionContext c, ProgramSelectionCandidates result, IProgramSelectionQueries queries)
    {
        if (c.Features.Single(SkillProgramEffectOp.UseSelectedCardsAs) is not { SourceBind: { } bind } ||
            c.SkillId != ChengJiChengCuiComposition.Tousui || bind != "bottom-cost-slash") return false;
        result.VariableInput = true;
        result.Cards = queries.Get<ITousuiSelectionQuery>().Cards(c.OwnerSeat).Select(x => x.Id).Order().ToArray();
        return true;
    }
    public void BuildTargets(ProgramSelectionContext c, ProgramSelectionCandidates result, IProgramSelectionQueries queries) =>
        new ConvertedCardSelectionSource().BuildTargets(c, result, queries);
}
internal interface IChengJiChengCuiHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal sealed class ChengJiChengCuiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) =>
        h.GetCapability<IChengJiChengCuiHost>().Resolve(f, op);
}
internal abstract class ChengJiChengCuiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new ChengJiChengCuiHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveXumingIssuedTurnEndUses ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry);
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) =>
        Op == SkillProgramEffectOp.ResolveXumingIssuedTurnEndUses && c.Frame.XumingEnding is { } r &&
        r.Source.SkillId == ChengJiChengCuiComposition.Xuming && r.Source.OwnerSeat == c.Frame.OwnerSeat &&
        r.Source.SkillInstanceId == c.Frame.SkillInstanceId && r.GameplayHash == c.Frame.GameplayHash && r.Promises.Count > 0;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.ResolveTousuiDodgeRequirement ? SkillProgramTriggerWindow.CardUseTargetsFinalized :
            Op == SkillProgramEffectOp.ResolveXumingDamagePromise ? SkillProgramTriggerWindow.BeforeDamageApplied : SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class ResolveTousuiDodgeRequirementDescriptor : ChengJiChengCuiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveTousuiDodgeRequirement; }
internal sealed class ResolveXumingDamagePromiseDescriptor : ChengJiChengCuiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveXumingDamagePromise; }
internal sealed class ResolveXumingIssuedTurnEndUsesDescriptor : ChengJiChengCuiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveXumingIssuedTurnEndUses; }
