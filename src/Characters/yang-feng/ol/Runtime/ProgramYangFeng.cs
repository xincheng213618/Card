using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum YangFengStage { DiscardChoice, DiscardChildren, RevealChildren, OwnerGainChildren, TargetGainChildren, CleanupChildren, QujiaChoice, QujiaGainChildren }
public sealed record YangFengMaterial(int CardId, CardKind PrintedKind, CardLocation From, int Slot, bool GeneralWeapon = false);
public sealed record YangFengMovement(int Index, YangFengStage Stage, string Reason, long Before, long After, int Count);
public sealed record YangFengStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int ActualTurn, int TurnOwner,
    int Phase, long ParentId, int TargetSeat, int HistoryCutoff, int CompletedTurns, int MaximumHp, int X, int RequiredDiscard) : IGameEvent;
public sealed record YangFengMaterialMovedEvent(long FrameId, YangFengStage Stage, int Index, YangFengMaterial Material, CardLocation To, long Sequence) : IGameEvent;
public sealed record YangFengMovementIssuedEvent(long FrameId, YangFengMovement Movement) : IGameEvent;
public sealed record YangFengCompletedEvent(long FrameId) : IGameEvent;
public sealed record YangFengReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int ActualTurn, int TurnOwner, int Phase,
    long ParentId, int TargetSeat, int HistoryCutoff, int CompletedTurns, int MaximumHp, int X, int RequiredDiscard, YangFengStage Stage,
    IReadOnlyList<YangFengMaterial> Candidates, IReadOnlyList<YangFengMaterial> Selected, IReadOnlyList<JiaweiRevealedEntity> Revealed, IReadOnlyList<YangFengMovement> Movements,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QujiaChoice = null)
{
    private IReadOnlyList<YangFengMaterial> _candidates = Array.AsReadOnly(Candidates.ToArray());
    public IReadOnlyList<YangFengMaterial> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<YangFengMaterial> _selected = Array.AsReadOnly(Selected.ToArray());
    public IReadOnlyList<YangFengMaterial> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<JiaweiRevealedEntity> _revealed = Array.AsReadOnly(Revealed.ToArray());
    public IReadOnlyList<JiaweiRevealedEntity> Revealed { get => _revealed; init => _revealed = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<YangFengMovement> _movements = Array.AsReadOnly(Movements.ToArray());
    public IReadOnlyList<YangFengMovement> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YangFengReceipt? YangFeng { get; init; } }
internal interface IYangFengHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class YangFengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new YangFengHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_,c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveJiaweiDiscardReveal ? [new ReadSelectedTarget()] : [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class YangFengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IYangFengHost>().Resolve(f,op); }
internal static class YangFengComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveJiaweiDiscardReveal or SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd;
    internal static void Activation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => Handles(e.Op))) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.ResolveJiaweiDiscardReveal, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.AnyLiving || a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": Jiawei requires a single zero-card any-living target activation per actual Play phase.");
    }
    internal static void Trigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => Handles(e.Op))) return;
        if (t.Effects is not [{ Op: SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            t.Window != SkillProgramTriggerWindow.AfterTurnEnded || t.Subject != SkillProgramTriggerSubject.Owner || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || !t.Optional ||
            t.UsageScope != SkillUsageScope.Game || t.UsageLimit != 1 || t.MarkerCost is not null || t.Condition is not { Kind: SkillProgramTriggerConditionKind.Not, Children: [{ Kind: SkillProgramTriggerConditionKind.OwnerKilledThisTurn }] })
            throw new InvalidOperationException(path + ": Qujia requires one optional game-limited actual own-turn end after a turn with no owner-attributed kill.");
    }
    [ProgramCatalogValidation(400)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => Handles(e.Op))) && !p.Triggers.Any(t => t.Effects.Any(e => Handles(e.Op)))) return;
        if (p.ViewAs.Count != 0 || (p.Id == "ol:jiawei" ? p.Activations.Count != 1 || p.Triggers.Count != 0 : p.Id != "ol:qujia" || p.Triggers.Count != 1 || p.Activations.Count != 0))
            throw new InvalidOperationException(path + ": Yang Feng requires his complete current ordinary-OL activation and limited trigger.");
        foreach (var a in p.Activations) Activation(path,a); foreach (var t in p.Triggers) Trigger(path,t);
    }
}
