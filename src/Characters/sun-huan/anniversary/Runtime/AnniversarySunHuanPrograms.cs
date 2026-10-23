using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversarySunHuanHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversarySunHuanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversarySunHuanHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeDrawInvocation | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op == SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw ? SkillProgramTriggerWindow.CardUseTargetsFinalized : SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class ResolveAnniversarySunHuanNijiDrawDescriptor : AnniversarySunHuanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw; }
internal sealed class ResolveAnniversarySunHuanNijiEndingDescriptor : AnniversarySunHuanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversarySunHuanNijiEnding; }
internal sealed class AnniversarySunHuanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversarySunHuanHost>().Begin(f, op);
}
public enum AnniversarySunHuanStage { Drawing, DrawChildren, Choice, IssuingUse, NativeUse, NativeTail, DiscardChildren }
// These are private rule receipts. They never enter public event history or player views.
public sealed record AnniversarySunHuanDraw(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner, NativeDrawOutcome Outcome);
public sealed partial class CharacterState
{
    private IReadOnlyList<AnniversarySunHuanDraw> _anniversaryNijiDraws = [];
    internal IReadOnlyList<AnniversarySunHuanDraw> AnniversaryNijiDraws { get => _anniversaryNijiDraws; set => _anniversaryNijiDraws = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversarySunHuanReceipt(CardConversionSource Source, string Hash, int Turn, int TurnOwner, long ParentId, SkillProgramEffectOp Operation, AnniversarySunHuanStage Stage)
{
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public AnniversarySunHuanNative? Native { get; init; }
    public bool Returned { get; init; }
    private IReadOnlyList<long> _nativeBatches = [];
    public IReadOnlyList<long> NativeBatches { get => _nativeBatches; init => _nativeBatches = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversarySunHuanNative(long FrameId, int Instruction, CardConversionSource Source, string Hash, int Turn, int Actor, int CardId, LegalAction Action, long UseId, long ActionId);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversarySunHuanReceipt? AnniversarySunHuan { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversarySunHuanNative? AnniversarySunHuanUse { get; init; } }
public sealed record AnniversarySunHuanStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner, long ParentId, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record AnniversarySunHuanDrawReturnedEvent(long FrameId, int Owner, int Turn, int ActualCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record AnniversarySunHuanUseIssuedEvent(long FrameId, long UseId, long ActionId, int CardId) : IGameEvent;
public sealed record AnniversarySunHuanUseReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversarySunHuanDiscardedEvent(long FrameId, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversarySunHuanNativeBatchEvent(long FrameId, long UseId, long BatchId) : IGameEvent;

internal static class AnniversarySunHuanComposition
{
    internal const string Skill = "anniversary:niji";
    [ProgramCatalogValidation(45400)] internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id != Skill && !p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw or SkillProgramEffectOp.ResolveAnniversarySunHuanNijiEnding)) return;
        if (p.Id != Skill || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.Triggers.Count != 2 ||
            p.Triggers[0] is not { Id: "target-draw", Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, OwnerRelation: SkillProgramCardActionOwnerRelation.Target, Optional: true, UsageScope: null, Condition.Kind: SkillProgramTriggerConditionKind.CardActionCategoryIs, Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw, Target: SkillProgramEffectTarget.Owner }] } ||
            !p.Triggers[0].Condition.CardCategories.OrderBy(x => x).SequenceEqual(new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick }.OrderBy(x => x)) ||
            p.Triggers[1] is not { Id: "ending-use-discard", Window: SkillProgramTriggerWindow.TurnEnding, Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving, Optional: false, IsSkillInvocation: false, UsageScope: null, Condition.Kind: SkillProgramTriggerConditionKind.Always, Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversarySunHuanNijiEnding, Target: SkillProgramEffectTarget.Owner }] })
            throw new InvalidOperationException(path + ": Niji requires every Basic/Trick actual target and each actual ending's exact-card use followed by discard.");
    }
}
