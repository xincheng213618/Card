using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryWangChangHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryWangChangDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new AnniversaryWangChangHandler(Op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override bool UsesConversionPolarity => Op == SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => Op == SkillProgramEffectOp.ResolveAnniversaryWangChangPingxi ? new(Triggered: ProgramTrackedFact.NativeLogicalDiscard) : default;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op == SkillProgramEffectOp.ResolveAnniversaryWangChangPingxi ? [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)] : [];
}
internal sealed class ResolveAnniversaryWangChangKaijiDescriptor : AnniversaryWangChangDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji; }
internal sealed class ResolveAnniversaryWangChangPingxiDescriptor : AnniversaryWangChangDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryWangChangPingxi; }
internal sealed class AnniversaryWangChangHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryWangChangHost>().Begin(frame, operation);
}
internal static class AnniversaryWangChangComposition
{
    internal const string Kaiji = "anniversary:kaiji", Pingxi = "anniversary:pingxi";
    [ProgramCatalogValidation(46500)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji or SkillProgramEffectOp.ResolveAnniversaryWangChangPingxi)) return;
        var common = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.BooleanStates.Count == 0 && p.Contributions.Count == 0;
        var kaiji = p.Id == Kaiji && p.Triggers.Count == 0 && p.Activations is [var a] && a.Id == "convert" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 && a.UsesPerPhase == 1 && a.UsesPerTurn is null &&
            a.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
        var pingxi = p.Id == Pingxi && p.Activations.Count == 0 && p.Triggers is [var t] && t.Id == "ending-discard-and-slashes" && t.Window == SkillProgramTriggerWindow.TurnEnding && t.Subject == SkillProgramTriggerSubject.Owner && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.Optional && t.IsSkillInvocation && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null &&
            t.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryWangChangPingxi, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
        if (!common || !kaiji && !pingxi) throw new InvalidOperationException(path + ": Wang Chang requires his exact phase conversion or own Ending discard-then-native-Slash composition.");
    }
}
public enum AnniversaryWangChangStage { OwnCards, Targets, ForeignCard, Drawing, MovementChildren, IssuingSlash, NativeSlash, ReturnedSlash, Completed }
public sealed record AnniversaryWangChangReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId, int Turn, int Phase, int Maximum, SkillPolarity Polarity, AnniversaryWangChangStage Stage)
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>()), _targets = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CardMovementRecord> _moves = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Moves { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
    public bool TargetsSealed { get; init; }
    public int Cursor { get; init; }
    public int SlashCursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryWangChangReceipt? AnniversaryWangChang { get; init; } }
public sealed record AnniversaryWangChangSlashOrigin(long FrameId, CardConversionSource Source, string Hash, int Cursor, long UseId, long ActionId, int TargetSeat);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryWangChangSlashOrigin? AnniversaryWangChangSlash { get; init; } }
public sealed record AnniversaryWangChangStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId, int Turn, int Phase, int Maximum, SkillPolarity Polarity) : IGameEvent;
public sealed record AnniversaryWangChangTargetsSealedEvent(long FrameId, int Count) : IGameEvent;
public sealed record AnniversaryWangChangTargetSelectedEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record AnniversaryWangChangDiscardPaidEvent(long FrameId, int Cursor, int SubjectSeat, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryWangChangDrawReturnedEvent(long FrameId, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryWangChangSlashIssuedEvent(long FrameId, int Cursor, long UseId, long ActionId, int TargetSeat) : IGameEvent;
public sealed record AnniversaryWangChangSlashReturnedEvent(long FrameId, int Cursor, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryWangChangCompletedEvent(long FrameId) : IGameEvent;
