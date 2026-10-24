using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiXqStage { Target, LoseSkill, LossChildren, RevokeChildren, CopyTarget, CopySkill, GrantChildren, Drawing, DrawChildren, Complete }
public sealed record ShiXqReceipt(CardConversionSource Source, string Hash, int Turn, int Round, long ParentId, SkillProgramEffectOp Operation, ShiXqStage Stage)
{
    public int Target { get; init; } = -1;
    public string? SelectedSkill { get; init; }
    public string? GrantId { get; init; }
    public string? GrantSource { get; init; }
    public string? GrantedInstance { get; init; }
    public bool Paid { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<string> _revokeIds = [];
    public IReadOnlyList<string> RevokeIds { get => _revokeIds; init => _revokeIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiXqReceipt? ShiXiaoQiao { get; init; } }
public sealed record ShiXqStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int Round, long ParentId, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record ShiXqHeyunPaidEvent(long FrameId, CardConversionSource Source, int Target, string LostSkillId) : IGameEvent;
public sealed record ShiXqGrantClearedEvent(long FrameId, int Owner, string GrantId, string SkillId, bool WasPresent) : IGameEvent;
public sealed record ShiXqCopiedEvent(long FrameId, CardConversionSource Source, string Hash, int Round, int Donor, string SkillId, string GrantId, string GrantSourceId, string SkillInstanceId) : IGameEvent;
public sealed record ShiXqDrawPaidEvent(long FrameId, int Recipient, long? DrawId, int ActualCount, bool Replaced, long Before, long After) : IGameEvent;
public sealed record ShiXqCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;

internal interface IShiXqHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp operation); }
internal abstract class ShiXqDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new ShiXqHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => operation == SkillProgramEffectOp.ResolveMobileShiXiaoQiaoYinhui ? [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)] : [];
}
internal sealed class ResolveMobileShiXiaoQiaoHeyunDescriptor() : ShiXqDescriptor(SkillProgramEffectOp.ResolveMobileShiXiaoQiaoHeyun);
internal sealed class ResolveMobileShiXiaoQiaoYinhuiDescriptor() : ShiXqDescriptor(SkillProgramEffectOp.ResolveMobileShiXiaoQiaoYinhui);
internal sealed class ShiXqHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IShiXqHost>().Begin(f, operation);
}
internal static class ShiXqComposition
{
    [ProgramCatalogValidation(50400)] internal static void Validate(string path, SkillProgram p)
    {
        var heyun = SkillProgramEffectOp.ResolveMobileShiXiaoQiaoHeyun; var yinhui = SkillProgramEffectOp.ResolveMobileShiXiaoQiaoYinhui;
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == heyun || e.Op == yinhui)) return;
        static bool One(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var valid = p.Id == "mobile:shi-heyun" ? p.Triggers.Count == 0 && p.Activations is [{ Id: "lose-for-draw", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerPhase: 2, UsesPerTurn: null } a] && One(a.Effects, heyun) :
            p.Id == "mobile:shi-yinhui" && p.Activations.Count == 0 && p.Triggers is [{ Id: "copy-current-skill", Window: SkillProgramTriggerWindow.RoundStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: true } t] && One(t.Effects, yinhui);
        if (!valid || p.ViewAs.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0 || p.Modifiers.Count != 0)
            throw new InvalidOperationException(path + ": Shi Xiao Qiao requires exactly twice-per-Play skill loss with native draw, and optional round-start source-scoped current-skill copying.");
    }
}
