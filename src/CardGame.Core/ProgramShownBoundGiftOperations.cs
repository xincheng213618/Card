namespace CardGame.Core;

public sealed record ProgramShownGiftCard(int CardId, SkillProgramCardCategory Category,
    int MovementSequence, CardLocation Source, CardLocation Destination);
public sealed record ProgramShownGiftReceipt(int InstructionIndex, long BatchId, int RecipientSeat,
    int ActualTurnNumber, int ActualTurnOwnerSeat, IReadOnlyList<ProgramShownGiftCard> Cards, long? ModifierGrantSequence);
public sealed record ShownBoundGiftCommittedEvent(long ParentFrameId, string SkillId, int OwnerSeat,
    int RecipientSeat, int ActualTurnNumber, int ActualTurnOwnerSeat, int ActualCount, int CategoryCount) : IGameEvent;

internal interface IShownBoundGiftProgramHost
{
    SkillProgramStepOutcome GiveShownBoundCardsAndGrantTurnHandLimit(ProgramSkillFrame frame, string sourceBind);
}
internal sealed class GiveShownBoundCardsAndGrantTurnHandLimitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveShownBoundCardsAndGrantTurnHandLimit;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveShownBoundCardsAndGrantTurnHandLimitHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GiveSelected, static (e,c) => c.ShownBoundGift(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), sourceBind:r.RequiredIdentifier("sourceBind"));
        RequireAlways(e,r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireOwnedCardSet(e.SourceBind!,SkillProgramEffectTarget.Owner,null,[CardZoneKind.Hand,CardZoneKind.Equipment]),
         new RequirePublicOwnedGiftSet(e.SourceBind!),new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving),
         new MoveCardSet(e.SourceBind!,null,SkillProgramCardDestination.SelectedTargetHand)];
}
internal sealed class GiveShownBoundCardsAndGrantTurnHandLimitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveShownBoundCardsAndGrantTurnHandLimit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host) =>
        ((IShownBoundGiftProgramHost)host).GiveShownBoundCardsAndGrantTurnHandLimit(f,e.SourceBind!);
}
