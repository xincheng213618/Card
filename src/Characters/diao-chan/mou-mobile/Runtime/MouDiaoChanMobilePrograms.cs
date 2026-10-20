using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouDiaoChanMobileStage { Targets, Cards, CostChildren, Duels, Drawing, DrawChildren }
public sealed record MouDiaoChanMobileReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, ActualDiscardRecoveryPhaseKey Phase)
{
    public MouDiaoChanMobileStage Stage { get; init; }
    public bool Paid { get; init; }
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>()), _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public int Index { get; init; }
    public long UseId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int DrawCount { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDiaoChanMobileReceipt? MouDiaoChanMobile { get; init; } }
public sealed record MouDiaoChanMobileRingOrigin(long FrameId, int Index, int ActorSeat, int TargetSeat, CardConversionSource Source, string GameplayHash);
public sealed partial record ProgramSelectedActorDuelOrigin
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDiaoChanMobileRingOrigin? MouDiaoChanRing { get; init; } }
public sealed record MouDiaoChanMobileStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouDiaoChanMobileRosterEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record MouDiaoChanMobileCostEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouDiaoChanMobileMovementEvent(long FrameId, int CardId, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouDiaoChanMobileDuelPlannedEvent(MouDiaoChanMobileRingOrigin Origin) : IGameEvent;
public sealed record MouDiaoChanMobileDuelIssuedEvent(MouDiaoChanMobileRingOrigin Origin, long UseId, long ActionId) : IGameEvent;
public sealed record MouDiaoChanMobileDuelReturnedEvent(long FrameId, int Index, long UseId) : IGameEvent;
public sealed record MouDiaoChanMobileBiyueEvent(long FrameId, int TurnNumber, int DamagedRoles, int RequestedDraw) : IGameEvent;
public sealed record MouDiaoChanMobileDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
internal interface IMouDiaoChanMobileHost { SkillProgramStepOutcome Start(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouDiaoChanMobileDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouDiaoChanMobileHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => Op == SkillProgramEffectOp.ResolveMouDiaoChanLijian
        ? new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard)
        : new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveMouDiaoChanBiyue
        ? [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)] : [];
}
internal sealed class MouDiaoChanLijianDescriptor : MouDiaoChanMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDiaoChanLijian; }
internal sealed class MouDiaoChanBiyueDescriptor : MouDiaoChanMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDiaoChanBiyue; }
internal sealed class MouDiaoChanMobileHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouDiaoChanMobileHost>().Start(f, op);
}
internal static class MouDiaoChanMobileComposition
{
    [ProgramCatalogValidation(1390)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id is not ("mou-mobile:lijian" or "mou-mobile:biyue") && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op is SkillProgramEffectOp.ResolveMouDiaoChanLijian or SkillProgramEffectOp.ResolveMouDiaoChanBiyue)) return;
        var valid = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 &&
            (p.Id == "mou-mobile:lijian" ? p.Triggers.Count == 0 && p.Activations is [var a] && a.Id == "ring-duels" &&
                a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 && a.TargetKind == SkillProgramTargetKind.AnyLiving &&
                a.UsesPerTurn is null && a.UsesPerPhase == 1 && a.UsesPerGame is null && Own(a.Effects, SkillProgramEffectOp.ResolveMouDiaoChanLijian) :
             p.Id == "mou-mobile:biyue" && p.Activations.Count == 0 && p.Triggers is [var t] && t.Id == "ending-damaged-roles" &&
                t.Window == SkillProgramTriggerWindow.TurnEnding && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional && t.OwnerRelation is null &&
                t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
                t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && Own(t.Effects, SkillProgramEffectOp.ResolveMouDiaoChanBiyue));
        if (!valid) throw new InvalidOperationException(path + ": Mou Diao Chan requires complete multi-participant Lijian and mandatory actual Ending Biyue.");
    }
    private static bool Own(IReadOnlyList<SkillProgramEffect> e, SkillProgramEffectOp op) => e is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } own] && own.Op == op;
}
