using System.Text.Json.Serialization;

namespace CardGame.Core;

// An optional policy on the existing sequential physical-acquisition primitive.
// Missing metadata retains the original maximum-HP, uncapped activity behavior.
public sealed partial class SkillProgramEffect
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CapGrowthByInitialPlayerCount { get; internal init; }
}

public sealed partial class GameEngine
{
    private int SequentialDeckKindGrowthRequestedCount(SkillProgramEffect effect, CharacterState owner) =>
        effect.NumberExpression == SkillProgramNumberExpression.OwnerMaxHp ? Math.Max(0, owner.MaxHp) : effect.Amount;

    private int SequentialDeckKindGrowthGrantAmount(CardConversionSource source, long frameId, int shortfall)
    {
        var program = _contentRegistry.GetSkill(source.SkillId).Program!;
        var effect = program.Triggers.Single(trigger => trigger.Id == source.BindingId).Effects[0];
        if (effect.CapGrowthByInitialPlayerCount != true) return shortfall;

        // Player slots are fixed at setup, unlike the live-player set. A named
        // skill's prior issued grants remain spent across skill-instance changes.
        var prior = ProgramEventHistory<SequentialDeckKindGrowthGrantedEvent>()
            .TakeWhile(item => item.ProgramFrameId != frameId)
            .Where(item => item.Source.OwnerSeat == source.OwnerSeat && item.Source.SkillId == source.SkillId)
            .Aggregate(0, (sum, item) => checked(sum + item.Amount));
        return Math.Min(shortfall, Math.Max(0, _players.Count - prior));
    }
}
