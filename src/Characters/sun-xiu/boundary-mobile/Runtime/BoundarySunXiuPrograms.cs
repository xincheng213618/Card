using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IBoundarySunXiuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveBoundarySunXiuXingxueDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundarySunXiuXingxue;
    public override ISkillProgramEffectHandler Handler { get; } = new BoundarySunXiuHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.SelectTarget, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered:ProgramTrackedFact.NativeDrawInvocation);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class BoundarySunXiuHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundarySunXiuXingxue;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IBoundarySunXiuHost>().Resolve(frame);
}
internal interface IBoundarySunXiuSelectionQuery : IProgramSelectionQuery { bool HasOwnedRegionCards(int seat); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget,53500,SkillProgramEffectOp.LoseOwnerSkillsAndGrant)]
internal sealed class BoundarySunXiuYanzhuTarget : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context,int target,IProgramSelectionQueries queries) =>
        context.SkillId != BoundarySunXiuComposition.Yanzhu || queries.Get<IBoundarySunXiuSelectionQuery>().HasOwnedRegionCards(target);
}
internal static class BoundarySunXiuComposition
{
    internal const string Yanzhu="boundary:yanzhu-mobile", Xingxue="boundary:xingxue-mobile",
        Upgraded="boundary:xingxue-mobile-upgraded", Zhaofu="boundary:zhaofu-mobile";
    [ProgramCatalogValidation(53500)]
    internal static void Validate(string path,SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t=>t.Effects).Concat(program.Activations.SelectMany(a=>a.Effects)).Any(e=>e.Op==SkillProgramEffectOp.ResolveBoundarySunXiuXingxue)) return;
        if (program.Id != Upgraded || program.Activations.Count != 0 || program.Triggers is not
            [{Id:"ending-draw-and-give-or-top",Window:SkillProgramTriggerWindow.TurnEnding,Subject:SkillProgramTriggerSubject.Owner,Optional:true,
                Effects:[{Op:SkillProgramEffectOp.ResolveBoundarySunXiuXingxue}]}])
            throw new InvalidOperationException(path+": upgraded mobile Xingxue requires its original owner's ending and frozen participant operation.");
    }
}
public enum BoundarySunXiuStage { Participants, Drawing, DrawChildren, Card, MoveChildren, Complete }
public sealed record BoundarySunXiuReceipt(CardConversionSource Source,string GameplayHash,long EndingId,int Quota,BoundarySunXiuStage Stage)
{
    private readonly IReadOnlyList<int> _participants=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Participants { get=>_participants; init=>_participants=Array.AsReadOnly(value.ToArray()); }
    public bool Sealed { get; init; }
    public int Index { get; init; }
    public int CardId { get; init; }
    public CardLocation? From { get; init; }
    public int Recipient { get; init; } = -1;
    public long Before { get; init; }
    public long After { get; init; }
    public long? DrawAttempt { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public BoundarySunXiuReceipt? BoundarySunXiu { get; init; }
}
public sealed record BoundarySunXiuStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long EndingId,int Quota):IGameEvent;
public sealed record BoundarySunXiuParticipantEvent(long FrameId,int Ordinal,int Participant):IGameEvent;
public sealed record BoundarySunXiuSealedEvent(long FrameId,int Count):IGameEvent;
public sealed record BoundarySunXiuDrawReturnedEvent(long FrameId,int Index,int Participant,long? Attempt,int Actual,long Before,long After):IGameEvent;
public sealed record BoundarySunXiuCardMovedEvent(long FrameId,int Index,int Participant,int CardId,CardLocation From,int Recipient,long Before,long After):IGameEvent;
public sealed record BoundarySunXiuCompletedEvent(long FrameId):IGameEvent;
