namespace CardGame.Core;

internal interface IDeckProgramHost
{
    SkillProgramStepOutcome ExecuteDeckProgram(SkillProgramEffect effect, ProgramSkillFrame frame);
}
internal sealed class ExchangeOwnedCardThroughDeckEndDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd;
    public override ISkillProgramEffectHandler Handler { get; } = new ExchangeOwnedCardThroughDeckEndHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move, static (effect, context) => context.DeckPrograms(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ConsumeSelectedCards(1)];
}
internal sealed class UseDeckSlashesThenShuffleDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseDeckSlashesThenShuffle;
    public override ISkillProgramEffectHandler Handler { get; } = new UseDeckSlashesThenShuffleHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (effect, context) => context.DeckPrograms(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "ignoreDistance", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            targetRestriction: r.Has("ignoreDistance") && r.RequiredBool("ignoreDistance") ? SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget : null);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class ExchangeOwnedCardThroughDeckEndHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => ((IDeckProgramHost)host).ExecuteDeckProgram(effect, frame);
}
public sealed class UseDeckSlashesThenShuffleHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseDeckSlashesThenShuffle;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => ((IDeckProgramHost)host).ExecuteDeckProgram(effect, frame);
}
public sealed record ProgramDeckEndExchange(int ProviderSeat, int CardId, CardLocation OriginalLocation, string Stage, bool? PlacedOnTop = null, int DrawIndex = 0);
public sealed record ProgramDeckSlashSequence(int TargetSeat, IReadOnlyList<int> DeclaredIds, int Limit, int? ActiveCardId = null, bool Shuffled = false)
{ public int UsedCount => DeclaredIds.Count; }
public sealed record DeckSlashSequenceShuffledEvent(long FrameId, int OwnerSeat, int Uses, int DrawPileCount) : IGameEvent;
