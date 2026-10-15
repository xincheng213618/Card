using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum FriendEventFlowStage { RecastCost, RecastDraw, DuelIssued, MaximumCost, TargetDiscard, AscendingCost, AscendingDraw, SuitHold, SuitDraw, Configuration }
public sealed record FriendEventFlow(int InstructionIndex, SkillProgramEffectOp Operation, FriendEventFlowStage Stage,
    int Rank = 0, int Count = 0, int TargetSeat = -1, Suit Suit = Suit.None, long? UseFrameId = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FriendEventFlow? FriendEventFlow { get; init; }
}
public sealed record FriendRecastDuelOrigin(long ProgramFrameId, int InstructionIndex, string SkillId, string BindingId,
    string SkillInstanceId, string GameplayHash, int OwnerSeat, int OriginalTargetSeat, int TurnNumber, int RecastCount, bool AttackStarted = false);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FriendRecastDuelOrigin? FriendRecastDuel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int FriendRecastDuelResponses { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ResumeFriendDuelResponseFrameId { get; init; }
}
public sealed record FriendDuelResponseFrame(long Id, long CardUseFrameId, int ResponderSeat, int OpponentSeat,
    int CardId, CardKind PrintedKind, CardLocation From, int PreviousResponses, FriendRecastDuelOrigin Origin)
    : ResolutionFrame(Id, ResolutionFrameKind.FriendDuelResponse, ResolutionFrameStep.ResolvingEffect);
public sealed record FriendEventConfiguredEvent(int OwnerSeat, string SkillId, string SkillInstanceId, string Option) : IGameEvent;
public sealed record AscendingHandRankPaidEvent(int OwnerSeat, string SkillId, int CardId, int Rank) : IGameEvent;
public sealed record UsedSuitHandHeldEvent(int OwnerSeat, string SkillId, long ActionId, int TurnNumber, Suit Suit) : IGameEvent;
public sealed record FriendMinimumHandUseEvent(long ActionId, int OwnerSeat, string SkillInstanceId, int Rank, Suit Suit) : IGameEvent;
public sealed record FriendDuelDiscardPaidEvent(long CardUseFrameId, int ResponderSeat, int CardId, CardLocation From, int ResponseCount) : IGameEvent;

internal interface IFriendEventProgramHost
{
    SkillProgramStepOutcome ExecuteFriendEvent(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal abstract class FriendEventOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new FriendEventOperationHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) =>
        {
            if (effect.Op == SkillProgramEffectOp.RecastOwnedCardsAndDuel) context.SelectedActorDuel();
            if (effect.Op == SkillProgramEffectOp.DiscardMaximumAndPunishHigherHand)
            { context.DiscardSelected(effect); context.ChooseOtherOwnedCardDiscard(new(SkillProgramEffectOp.ChooseOtherOwnedCardDiscard, SkillProgramEffectTarget.SelectedTarget, 1, effect.Condition)); }
            if (effect.Op is SkillProgramEffectOp.RecastOwnedCardsAndDuel or SkillProgramEffectOp.DiscardAscendingMaximumAndDraw or SkillProgramEffectOp.HoldUsedSuitHandAndDraw)
                context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, effect.Op == SkillProgramEffectOp.DiscardAscendingMaximumAndDraw ? 3 : 1, effect.Condition));
        });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.RecastOwnedCardsAndDuel => [new ConsumeSelectedCards(0), new ReadTargetSet(1, 1)],
        SkillProgramEffectOp.DiscardMaximumAndPunishHigherHand => [new ConsumeSelectedCards(1), new ReadTargetSet(1, 1)],
        SkillProgramEffectOp.ConfigureFriendDuelCategories or SkillProgramEffectOp.ConfigureFriendUseSuits => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.DiscardAscendingMaximumAndDraw => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
        SkillProgramEffectOp.HoldUsedSuitHandAndDraw => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)],
        _ => []
    };
}
internal sealed class RecastOwnedCardsAndDuelDescriptor : FriendEventOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecastOwnedCardsAndDuel; }
internal sealed class DiscardMaximumAndPunishHigherHandDescriptor : FriendEventOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardMaximumAndPunishHigherHand; }
internal sealed class DiscardAscendingMaximumAndDrawDescriptor : FriendEventOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardAscendingMaximumAndDraw; }
internal sealed class HoldUsedSuitHandAndDrawDescriptor : FriendEventOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.HoldUsedSuitHandAndDraw; }
internal sealed class ConfigureFriendDuelCategoriesDescriptor : FriendEventOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ConfigureFriendDuelCategories; }
internal sealed class ConfigureFriendUseSuitsDescriptor : FriendEventOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ConfigureFriendUseSuits; }
internal sealed class FriendEventOperationHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IFriendEventProgramHost>().ExecuteFriendEvent(frame, operation);
}
