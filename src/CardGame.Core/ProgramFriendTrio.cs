using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum FriendTrioStage { Branch, Mode, Prediction, Reveal, Reward, Discard, RemoveMarks, Benefit, Movement, HpLoss, Settle, FullReward, Complete, EquipmentUse }
public sealed record FriendPredictionStartedEvent(long ProgramFrameId, int OwnerSeat, string SkillInstanceId, int Count) : IGameEvent;
public sealed record FriendPredictionObservedEvent(long PredictionId, long ActionId, int Index, bool Correct, int CorrectCount) : IGameEvent;
public sealed record FriendPredictionSettledEvent(long PredictionId, int OwnerSeat, int Correct, int Total, bool Revealed, int NextCount) : IGameEvent;
public sealed record FriendPredictionRevealedEvent(long PredictionId, int OwnerSeat, IReadOnlyList<string> Predictions) : IGameEvent;
public sealed record FriendQihuiUnlimitedArmedEvent(long ProgramFrameId, int OwnerSeat) : IGameEvent;
public sealed record FriendXuanjianGeneratedEvent(long ProgramFrameId, int OwnerSeat, int CardId) : IGameEvent;
public sealed record FriendXuanjianUseIssuedEvent(long ProgramFrameId, long UseFrameId, long ActionId, int CardId) : IGameEvent;
public sealed record FriendXuanjianUseReturnedEvent(long ProgramFrameId, long UseFrameId, long ActionId, int CardId) : IGameEvent;
public sealed record FriendTrioMovementPaidEvent(long ProgramFrameId, SkillProgramEffectOp Operation, long Before, long After) : IGameEvent;
public sealed record FriendPredictionPolicy(long PredictionId, int OwnerSeat, string SkillInstanceId, string GameplayHash,
    string Mode, IReadOnlyList<string> Predictions, int Index = 0, int Correct = 0, int Drawn = 0, bool Revealed = false)
{
    private readonly IReadOnlyList<string> _predictions = Array.AsReadOnly(Predictions.ToArray());
    public IReadOnlyList<string> Predictions { get => _predictions; init => _predictions = Array.AsReadOnly(value.ToArray()); }
}
public sealed record FriendPredictionView(int OwnerSeat, long PredictionId, string Mode,
    IReadOnlyList<string> Values, int Index, int Correct, bool Revealed)
{
    private readonly IReadOnlyList<string> _values = Array.AsReadOnly(Values.ToArray());
    public IReadOnlyList<string> Values { get => _values; init => _values = Array.AsReadOnly(value.ToArray()); }
}
public sealed record FriendTrioReceipt(int InstructionIndex, SkillProgramEffectOp Operation, FriendTrioStage Stage,
    IReadOnlyList<string> Values, IReadOnlyList<int> Cards, int Count = 0, string? Mode = null,
    FriendPredictionPolicy? Policy = null, bool StartNewPrediction = false, long Before = 0, long After = 0,
    FriendTrioStage NextStage = FriendTrioStage.Complete, bool FullVerification = false,
    long? IssuedUseFrameId = null, long? IssuedActionId = null)
{
    private readonly IReadOnlyList<string> _values = Array.AsReadOnly(Values.ToArray());
    public IReadOnlyList<string> Values { get => _values; init => _values = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Cards.ToArray());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FriendTrioReceipt? FriendTrio { get; init; }
}
internal interface IFriendTrioProgramHost
{
    SkillProgramStepOutcome ExecuteFriendTrio(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal abstract class FriendTrioDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new FriendTrioHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.BeginFriendPrediction => [new RequireTriggerWindows([SkillProgramTriggerWindow.RoundStarting, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow])],
        SkillProgramEffectOp.ObserveFriendPrediction or SkillProgramEffectOp.GainFriendCategoryMark => [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseCommitted, SkillProgramTriggerWindow.CardResponseAccepted]), new RequireContext(ProgramContextCapability.CardAction)],
        SkillProgramEffectOp.EquipFriendXuanjian => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.ReclaimFriendXuanjian => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived), new RequireContext(ProgramContextCapability.MovementSource)],
        SkillProgramEffectOp.ExchangeFriendGainForDiscard => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained), new RequireContext(ProgramContextCapability.MovementSource)],
        SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)],
        _ => throw new InvalidOperationException("Unknown friend operation resources.")
    };
}
internal sealed class BeginFriendPredictionDescriptor : FriendTrioDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BeginFriendPrediction; }
internal sealed class ObserveFriendPredictionDescriptor : FriendTrioDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObserveFriendPrediction; }
internal sealed class EquipFriendXuanjianDescriptor : FriendTrioDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.EquipFriendXuanjian; }
internal sealed class ReclaimFriendXuanjianDescriptor : FriendTrioDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReclaimFriendXuanjian; }
internal sealed class GainFriendCategoryMarkDescriptor : FriendTrioDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GainFriendCategoryMark; }
internal sealed class ExchangeFriendGainForDiscardDescriptor : FriendTrioDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeFriendGainForDiscard; }
internal sealed class UseFriendDeckSuitsAtPlayEndDescriptor : FriendTrioDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd; }
internal sealed class FriendTrioHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IFriendTrioProgramHost>().ExecuteFriendTrio(f, Op);
}

internal static class FriendTrioComposition
{
    public static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        var owned = t.Effects.Where(e => (int)e.Op is >= 20100 and <= 20104).ToArray();
        if (owned.Length == 0) return;
        if (t.Effects.Count != 1 || owned[0].Target != SkillProgramEffectTarget.Owner ||
            owned[0].Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": friend skills require a single owner instruction.");
        var valid = owned[0].Op switch
        {
            SkillProgramEffectOp.BeginFriendPrediction => t.Window is SkillProgramTriggerWindow.RoundStarting or SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                t.Subject == SkillProgramTriggerSubject.Owner && t.Optional && t.UsageScope == SkillUsageScope.Round &&
                t.UsageLimit == 1 && t.NamedUsageGroup is not null,
            SkillProgramEffectOp.ObserveFriendPrediction => t.Window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted &&
                t.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && !t.Optional && t.SingleActionInstance && t.UsageScope is null,
            SkillProgramEffectOp.EquipFriendXuanjian => t.Window == SkillProgramTriggerWindow.GameStarting && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional,
            SkillProgramEffectOp.ReclaimFriendXuanjian => t.Window == SkillProgramTriggerWindow.DiscardPileReceived && t.Subject == SkillProgramTriggerSubject.Owner &&
                t.Optional && t.MovementOccurrence == SkillProgramMovementOccurrence.PerCard && t.CardKinds.SequenceEqual([CardKind.Xuanjian]),
            SkillProgramEffectOp.GainFriendCategoryMark => t.Window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted &&
                t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && !t.Optional && t.SingleActionInstance && t.UsageScope is null,
            _ => false
        };
        if (!valid) throw new InvalidOperationException(path + ": friend instruction requires its exact native trigger and owner relation.");
    }
}
