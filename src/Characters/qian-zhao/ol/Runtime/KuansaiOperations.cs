using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum KuansaiStage { Target, Benefit, Children }
public sealed record KuansaiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurn, int TurnOwnerSeat, long UseId, long ActionId, int ThresholdHp, KuansaiStage Stage = KuansaiStage.Target,
    int? ChooserSeat = null, bool? Gift = null, int? CardId = null, CardLocation? From = null, long Before = 0, long After = 0)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record KuansaiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwnerSeat, long UseId, long ActionId, int ThresholdHp, int TargetCount) : IGameEvent;
public sealed record KuansaiTargetChosenEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record KuansaiPaidEvent(long FrameId, int ChooserSeat, bool Gift, int? CardId, CardLocation? From, long Before, long After) : IGameEvent;
internal sealed class ResolveKuansaiTargetChoiceDescriptor() : QianZhaoDescriptor(SkillProgramEffectOp.ResolveKuansaiTargetChoice)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]; }
