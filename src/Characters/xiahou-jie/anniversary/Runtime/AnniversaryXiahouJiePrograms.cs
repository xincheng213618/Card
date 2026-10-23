using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryXiahouJieHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryXiahouJieDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler => new AnniversaryXiahouJieHandler(operation);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(operation == SkillProgramEffectOp.ResolveAnniversaryXiahouJieLiedan ? SkillProgramTriggerWindow.TurnStartBeforeNormalFlow : SkillProgramTriggerWindow.AfterTurnEnded)];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveAnniversaryXiahouJieLiedanDescriptor() : AnniversaryXiahouJieDescriptor(SkillProgramEffectOp.ResolveAnniversaryXiahouJieLiedan);
internal sealed class ResolveAnniversaryXiahouJieZhuangdanDescriptor() : AnniversaryXiahouJieDescriptor(SkillProgramEffectOp.ResolveAnniversaryXiahouJieZhuangdan);
internal sealed class AnniversaryXiahouJieHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryXiahouJieHost>().Begin(frame, operation);
}
internal static class AnniversaryXiahouJieComposition
{
    internal const string Liedan="anniversary:liedan", Zhuangdan="anniversary:zhuangdan", Compare="sequential-preparation", Suppress="unique-most-until-own-end";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveAnniversaryXiahouJieLiedan or SkillProgramEffectOp.ResolveAnniversaryXiahouJieZhuangdan;
    [ProgramCatalogValidation(45500)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t=>t.Effects).Any(e=>Handles(e.Op))) return;
        if (p.Id is not (Liedan or Zhuangdan) || p.Triggers.Count!=1 || p.Activations.Count!=0 || p.ViewAs.Count!=0 || p.Modifiers.Count!=0 || p.CardPolicies.Count!=0 || p.CardIdentities.Count!=0 || p.BooleanStates.Count!=0 || p.Contributions.Count!=0 || !Exact(p.Id,p.Triggers.Single()))
            throw new InvalidOperationException(path+": Xiahou Jie requires his exact mandatory sequential preparation or unique-hand actual-turn-end trigger.");
    }
    private static bool Exact(string id, SkillProgramTrigger t) => !t.Optional && t.IsSkillInvocation && t.OwnerRelation is null && t.Priority==0 &&
        t.Subject==SkillProgramTriggerSubject.Owner && t.Condition.Kind==SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null &&
        t.CardKinds.Count==0 && t.CardCategories.Count==0 && t.Suits.Count==0 && t.SourceZones.Count==0 && t.DestinationZones.Count==0 && t.SourceSkillId is null && !t.IncludeResponseUses && t.DamageOccurrence is null &&
        t.Effects is [{Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always} e] &&
        (id==Liedan && t.Id==Compare && t.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && t.TurnOwnerScope==SkillProgramTurnOwnerScope.AnyLiving && e.Op==SkillProgramEffectOp.ResolveAnniversaryXiahouJieLiedan ||
         id==Zhuangdan && t.Id==Suppress && t.Window==SkillProgramTriggerWindow.AfterTurnEnded && t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving && e.Op==SkillProgramEffectOp.ResolveAnniversaryXiahouJieZhuangdan);
}
public enum AnniversaryXiahouJieStage { Comparing, Drawing, DrawChildren, MaximumChildren, HpChildren, DirectDeath }
public sealed record AnniversaryXiahouJieComparison(int Index,int? OwnerValue,int? OtherValue)
{ public bool? Greater => OwnerValue is {} a && OtherValue is {} b ? a>b : null; }
public sealed record AnniversaryXiahouJieDraw(int Index,NativeDrawOutcome Outcome);
public sealed record AnniversaryXiahouJieReceipt(CardConversionSource Source,string Hash,long ParentId,int ActualTurn,int TurnOwner,AnniversaryXiahouJieStage Stage,
    int Cursor=0,bool HpPaid=false,int HpBefore=0,int HpAfter=0,int ActualLoss=0,int MaximumBefore=0,int MaximumAfter=0,int DeathMarks=0)
{
    private readonly IReadOnlyList<AnniversaryXiahouJieComparison> _comparisons=Array.AsReadOnly(Array.Empty<AnniversaryXiahouJieComparison>());
    private readonly IReadOnlyList<AnniversaryXiahouJieDraw> _draws=Array.AsReadOnly(Array.Empty<AnniversaryXiahouJieDraw>());
    public IReadOnlyList<AnniversaryXiahouJieComparison> Comparisons { get=>_comparisons; init=>_comparisons=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<AnniversaryXiahouJieDraw> Draws { get=>_draws; init=>_draws=Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryXiahouJieStartedEvent(long FrameId,CardConversionSource Source,string Hash,long ParentId,int ActualTurn,int TurnOwner,bool OwnPreparation,int Marks) : IGameEvent;
public sealed record AnniversaryXiahouJieComparedEvent(long FrameId,int Index,int? OwnerValue,int? OtherValue) : IGameEvent;
public sealed record AnniversaryXiahouJieDrawReturnedEvent(long FrameId,int Index,int ActualCount,long? AttemptId,long Before,long After) : IGameEvent;
public sealed record AnniversaryXiahouJieMaximumEvent(long FrameId,int Before,int After) : IGameEvent;
public sealed record AnniversaryXiahouJieHpPaidEvent(long FrameId,int Before,int After,int ActualLoss) : IGameEvent;
public sealed record AnniversaryXiahouJieCompletedEvent(long FrameId) : IGameEvent;
public sealed record AnniversaryXiahouJieSuppressedEvent(long FrameId,CardConversionSource Source,string Hash,int ActualTurn,int TurnOwner) : IGameEvent;
public sealed record AnniversaryXiahouJieSuppressionExpiredEvent(long FrameId,int OwnerSeat,int ActualTurn) : IGameEvent;
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public AnniversaryXiahouJieReceipt? AnniversaryXiahouJie { get;init; } }
