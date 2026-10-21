using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MobileWulingAnimal { Tiger, Crane, Bear, Ape, Deer }
public enum MobileHuaTuoStage { Recipient, Order, Entry, EntryChildren, DeerRecoveryChildren, Drawing, Ape, Children, RecoverAll, Finished }
public sealed record MobileHuaTuoReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int InstructionIndex, MobileHuaTuoStage Stage, int TargetSeat = -1, long CourseId = 0, int AnimalIndex = 0,
    int Cursor = 0, long Before = 0, long After = 0)
{
    private IReadOnlyList<MobileWulingAnimal> _order = Array.AsReadOnly(Array.Empty<MobileWulingAnimal>());
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>()), _recipients = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<MobileWulingAnimal> Order { get => _order; init => _order = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileHuaTuoReceipt? MobileHuaTuo { get; init; }
}
internal sealed record MobileWulingCourse(CardConversionSource Source, int TargetSeat)
{
    private IReadOnlyList<MobileWulingAnimal> _order = Array.AsReadOnly(Array.Empty<MobileWulingAnimal>());
    internal IReadOnlyList<MobileWulingAnimal> Order { get => _order; init => _order = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MobileWulingTaughtEvent(long CourseId, CardConversionSource Source, int TargetSeat) : IGameEvent;
public sealed record MobileWulingAdvancedEvent(long FrameId, long CourseId, int Index, bool Active) : IGameEvent;
public sealed record MobileWulingGrantEvent(long CourseId, int TargetSeat, string SkillId, string GrantId, bool Added) : IGameEvent;
public sealed record MobileWulingGrantRevokedEvent(long CourseId, int TargetSeat, string SkillId, string GrantId, string SkillInstanceId, string SourceId) : IGameEvent;
public sealed record MobileHuaTuoPaidEvent(long FrameId, int Cursor, string Kind, int TargetSeat, long Before, long After) : IGameEvent;
internal interface IMobileHuaTuoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MobileHuaTuoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MobileHuaTuoHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class TeachMobileHuaTuoDescriptor : MobileHuaTuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.TeachMobileShenHuaTuoWuling; }
internal sealed class AdvanceMobileHuaTuoDescriptor : MobileHuaTuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AdvanceMobileShenHuaTuoWuling; }
internal sealed class DepositMobileHuaTuoDescriptor : MobileHuaTuoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class RecoverMobileHuaTuoDescriptor : MobileHuaTuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverMobileShenHuaTuoYouyi; }
internal sealed class MobileHuaTuoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IMobileHuaTuoHost>().Resolve(f, op);
}
internal static class MobileHuaTuoComposition
{
    [ProgramCatalogValidation(39100)]
    internal static void Validate(string path, SkillProgram p)
    {
        static bool Effect(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [{ Target: SkillProgramEffectTarget.Owner } e] &&
            e.Op == op && e.Condition.Kind == SkillProgramConditionKind.Always;
        static bool Activation(SkillProgram p, SkillProgramEffectOp op, int limit) => p.Activations is [var a] &&
            a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 && a.UsesPerPhase == limit && Effect(a.Effects, op);
        var own = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is
            SkillProgramEffectOp.TeachMobileShenHuaTuoWuling or SkillProgramEffectOp.AdvanceMobileShenHuaTuoWuling or
            SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi or SkillProgramEffectOp.RecoverMobileShenHuaTuoYouyi);
        if (!own && p.Id != "mobile-god:wuling-deer") return;
        var valid = p.Modifiers.Count == 0 && p.ViewAs.Count == 0 && (p.Id switch
        {
            "mobile-god:wuling" => Activation(p, SkillProgramEffectOp.TeachMobileShenHuaTuoWuling, 2) && p.Triggers.Count == 0 && p.CardPolicies.Count == 0,
            "mobile-god:youyi" => Activation(p, SkillProgramEffectOp.RecoverMobileShenHuaTuoYouyi, 1) && p.CardPolicies.Count == 0 &&
                p.Triggers is [{ Window: SkillProgramTriggerWindow.DiscardPhaseEnded, Optional: true, AllowOwnDiscardPhaseEnded: true,
                    TurnOwnerScope: SkillProgramTurnOwnerScope.Own } t] && Effect(t.Effects, SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi),
            "mobile-god:wuling-effects" => p.Activations.Count == 0 && p.CardPolicies.Count == 0 && p.Triggers.Count == 2 &&
                p.Triggers.SingleOrDefault(t => t.Id == "next-spirit") is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                    Optional: false, IsSkillInvocation: false } cycle && Effect(cycle.Effects, SkillProgramEffectOp.AdvanceMobileShenHuaTuoWuling) &&
                p.Triggers.SingleOrDefault(t => t.Id == "bear-first-damage") is { Window: SkillProgramTriggerWindow.BeforeDamageApplied,
                    Optional: false, IsSkillInvocation: false, UsageScope: SkillUsageScope.Turn, UsageLimit: 1, Effects: [{ Op: SkillProgramEffectOp.ReduceCurrentDamage, Amount: 1 }] },
            "mobile-god:wuling-deer" => p.Activations.Count == 0 && p.Triggers.Count == 0 && p.CardPolicies is [{ Kind: SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget }],
            _ => false
        });
        if (!valid) throw new InvalidOperationException(path + ": Mobile Shen Hua Tuo requires its complete ordered teaching, phase discards, once-turn Bear and shared-area payment composition.");
    }
}
