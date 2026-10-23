using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryFengXiHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryFengXiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryFengXiHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveAnniversaryFengXiYusui ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => Op == SkillProgramEffectOp.ResolveAnniversaryFengXiYusui ? new(Triggered: ProgramTrackedFact.NativeLogicalDiscard) : default;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveAnniversaryFengXiYusui ?
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)] : [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class ResolveAnniversaryFengXiYusuiDescriptor : AnniversaryFengXiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryFengXiYusui; }
internal sealed class ResolveAnniversaryFengXiBoyanDescriptor : AnniversaryFengXiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryFengXiBoyan; }
internal sealed class AnniversaryFengXiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryFengXiHost>().Begin(f, op);
}
public enum AnniversaryFengXiStage { Choice, DiscardChoice, HealthReady, HealthApplying, HealthChildren, MovementChildren, Drawing, DrawChildren }
public sealed record AnniversaryFengXiReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, int Instruction,
    int Turn, long? ParentId, long? UseId, long? ActionId, int Target, AnniversaryFengXiStage Stage)
{
    public int Amount { get; init; }
    public int? OwnerHp { get; init; }
    public int? TargetHp { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryFengXiReceipt? AnniversaryFengXi { get; init; } }
public sealed record AnniversaryFengXiStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, int Instruction, int Turn, long? ParentId, long? UseId, long? ActionId, int Target) : IGameEvent;
public sealed record AnniversaryFengXiBranchEvent(long FrameId, string Branch, int Target) : IGameEvent;
public sealed record AnniversaryFengXiPaidEvent(long FrameId, int Target, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryFengXiHealthIssuedEvent(long FrameId, int Target, int Amount, int OwnerHp, int TargetHp) : IGameEvent;
public sealed record AnniversaryFengXiDrawReturnedEvent(long FrameId, int Target, int Requested, int Actual, long Before, long After, long? AttemptId) : IGameEvent;

internal static class AnniversaryFengXiComposition
{
    internal const string Yusui = "anniversary:yusui", Boyan = "anniversary:boyan";
    [ProgramCatalogValidation(44500)] internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id is not (Yusui or Boyan) && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryFengXiYusui or SkillProgramEffectOp.ResolveAnniversaryFengXiBoyan)) return;
        bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always && e.TargetReference is null && e.ActorReference is null;
        var valid = p.Id == Yusui ? p.Activations.Count == 0 && p.Triggers is [{ Id: "black-other-target", Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, OwnerRelation: SkillProgramCardActionOwnerRelation.Target,
            Optional: true, UsageScope: SkillUsageScope.Turn, UsageLimit: 1, Condition.Kind: SkillProgramTriggerConditionKind.CardActionCardIsBlack,
            Effects: [{ Op: SkillProgramEffectOp.LoseHp, Target: SkillProgramEffectTarget.Owner, Amount: 1 }, var y] }] && Plain(y, SkillProgramEffectOp.ResolveAnniversaryFengXiYusui) :
            p.Id == Boyan && p.Triggers.Count == 0 && p.Activations is [{ Id: "fill-and-seal-hand", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerPhase: 1, UsesPerTurn: null,
                Effects: [var b, { Op: SkillProgramEffectOp.GrantTurnHandCardProhibition, Target: SkillProgramEffectTarget.SelectedTarget }] }] && Plain(b, SkillProgramEffectOp.ResolveAnniversaryFengXiBoyan);
        if (!valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Feng Xi requires his once-per-turn black other-user target cost and once-per-Play capped total-Hand refill followed by Use/Played prohibition.");
    }
}
