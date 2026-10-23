using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IMobileShenMaChaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MobileShenMaChaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MobileShenMaChaoHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch {
        SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei => SkillProgramTriggerWindow.CardUseTargetsFinalized,
        SkillProgramEffectOp.ResolveMobileShenMaChaoJimie => SkillProgramTriggerWindow.PlayEnding,
        _ => SkillProgramTriggerWindow.BeforeDamageApplied })];
}
internal sealed class MobileMaChaoTingweiDescriptor : MobileShenMaChaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei; }
internal sealed class MobileMaChaoJimieDescriptor : MobileShenMaChaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShenMaChaoJimie; }
internal sealed class MobileMaChaoYuliDescriptor : MobileShenMaChaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShenMaChaoYuli; }
internal sealed class MobileShenMaChaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IMobileShenMaChaoHost>().Resolve(f, op);
}
public enum MobileMaChaoStage { Target, Options, Execute, Gift, Children, Drawing, Damage }
public sealed record MobileMaChaoReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    SkillProgramEffectOp Op, MobileMaChaoStage Stage, long UseId = 0, long ActionId = 0, int Target = -1,
    int Mask = -1, int Cursor = 0, int DrawCount = 0, int Damage = 0, long Before = 0, long After = 0,
    uint RandomBefore = 0, uint RandomAfter = 0)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileMaChaoReceipt? MobileShenMaChao { get; init; } }
public sealed record MobileTingweiBoost(long ProgramFrameId, CardConversionSource Source, long UseId, long ActionId, int Target);
public sealed partial record CardUseFrame
{
    private IReadOnlyList<MobileTingweiBoost>? _mobileTingweiBoosts;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<MobileTingweiBoost>? MobileTingweiBoosts
    { get => _mobileTingweiBoosts; init => _mobileTingweiBoosts = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record MobileTingweiStartedEvent(long FrameId, CardConversionSource Source, long UseId, long ActionId) : IGameEvent;
public sealed record MobileTingweiSelectedEvent(long FrameId, int Target, int Mask, int Paid) : IGameEvent;
public sealed record MobileTingweiSuppressedEvent(long FrameId, CardConversionSource Source, int Target, int ActualTurn) : IGameEvent;
public sealed record MobileTingweiExpiredEvent(long FrameId, int Target, int ActualTurn) : IGameEvent;
public sealed record MobileTingweiBoostedEvent(MobileTingweiBoost Boost) : IGameEvent;
public sealed record MobileMaChaoMovedEvent(long FrameId, string Mode, int Target, long Before, long After) : IGameEvent;
public sealed record MobileMaChaoJimieUsedEvent(long FrameId, CardConversionSource Source, int Target, int Paid, int ActualTurn) : IGameEvent;
public sealed record MobileMaChaoYuliExecutedEvent(long FrameId, int Owner, int Part, long JimieUseFrameId) : IGameEvent;
public sealed record MobileMaChaoJimieRefreshedEvent(long UseFrameId, int Owner) : IGameEvent;
public sealed record MobileMaChaoDrawReturnedEvent(long FrameId, int Owner, int Requested, int Actual, long Before, long After) : IGameEvent;
internal static class MobileShenMaChaoComposition
{
    [ProgramCatalogValidation(40625)] internal static void Validate(string path, SkillProgram p)
    {
        var ops = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Select(e => e.Op).ToArray();
        if (!ops.Any(op => op is SkillProgramEffectOp.ResolveMobileShenMaChaoYuli or SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei or SkillProgramEffectOp.ResolveMobileShenMaChaoJimie)) return;
        static bool One(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var plain = p.Activations.Count == 0 && p.Modifiers.Count == 0 && p.ViewAs.Count == 0 && p.CardPolicies.Count == 0;
        var valid = p.Id switch {
            "mobile-god:yuli" => p.Triggers.Count == 2 && p.Triggers.All(t => !t.Optional && t.IsSkillInvocation && t.Window == SkillProgramTriggerWindow.BeforeDamageApplied && One(t, SkillProgramEffectOp.ResolveMobileShenMaChaoYuli)) &&
                p.Triggers.Any(t => t.Id == "source-thunder" && t.Subject == SkillProgramTriggerSubject.Any) &&
                p.Triggers.Any(t => t.Id == "prevent-thunder" && t.Subject == SkillProgramTriggerSubject.DamageTarget && t.Condition.Kind == SkillProgramTriggerConditionKind.DamageNatureIs && t.Condition.DamageNature == DamageNature.Thunder),
            "mobile-god:tingwei" => p.Triggers is [{ Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, Optional: true, IsSkillInvocation: true } t] && One(t, SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei),
            "mobile-god:jimie" => p.Triggers is [{ Window: SkillProgramTriggerWindow.PlayEnding, Subject: SkillProgramTriggerSubject.Owner, Optional: true, IsSkillInvocation: true } t] && One(t, SkillProgramEffectOp.ResolveMobileShenMaChaoJimie),
            _ => false };
        if (!plain || !valid) throw new InvalidOperationException(path + ": Mobile Shen Ma Chao requires actual source/prevention damage windows, one whole Slash target window and rechargeable Play-ending Jimie.");
    }
}
