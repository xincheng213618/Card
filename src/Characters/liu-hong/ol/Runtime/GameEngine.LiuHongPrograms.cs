namespace CardGame.Core;

// 鬻爵 evidence: one scalar record per accepted invocation. The abolished slot,
// the card recipient and the given card id are all public; the 执笏 grant is
// rebuilt by replaying the accepted command, so the event carries no hidden ids
// beyond the given card's public hand-to-hand movement.
public sealed record ProgramYujueInvokedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, string AbolishedSlot, int TargetSeat, int GivenCardId) : IGameEvent;
public sealed record ProgramYujueSlotPaidEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int InstructionIndex, string AbolishedSlot) : IGameEvent;
public sealed record ProgramYujueGiftPaidEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int InstructionIndex, int TargetSeat, int GivenCardId, int MovementSequence) : IGameEvent;

// Shared game-long damage bonus: arming evidence for one seat. Any content skill
// can arm its owner through this generic event; FinalizeAttackDamageAmount adds
// the armed amount to every damage the seat deals (chain propagation included),
// so the armed state is derived from committed history on cold recovery.
public sealed record ProgramGameDamageBonusArmedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, int Amount) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public YujuePendingState? YujuePending { get; init; }
}

// The suspension receipt of one accepted 鬻爵 invocation. AbolishedSlot is set
// once the slot prompt resolves; TargetSeat joins for the card-give prompt.
public enum YujuePendingStage { SlotPaid, GiftPaid }
public sealed record YujuePendingState(string AbolishedSlot, int? TargetSeat,
    CardConversionSource Source, string GameplayHash, int InstructionIndex,
    YujuePendingStage Stage = YujuePendingStage.SlotPaid, int? GivenCardId = null, int? GiftMovementSequence = null);

public sealed partial class GameEngine
{
    internal static readonly EquipmentSlot[] YujueSlotOrder =
        [EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.OffensiveHorse,
         EquipmentSlot.DefensiveHorse, EquipmentSlot.Treasure];

    // Abolish preference for AI: the least combat-relevant slot first; empty
    // slots are preferred over equipped ones at the same preference rank.
    private static readonly EquipmentSlot[] YujueAiAbolishPreference =
        [EquipmentSlot.Treasure, EquipmentSlot.DefensiveHorse, EquipmentSlot.OffensiveHorse,
         EquipmentSlot.Armor, EquipmentSlot.Weapon];

    // Shared arming: emits the committed evidence the damage pipeline reads.
    // The evidence attributes to the skill whose clause arms the bonus, so the
    // damage-time log names the right skill.
    private void ArmProgramGameDamageBonus(ProgramSkillFrame frame, string skillId, int ownerSeat, int amount)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var bindingId = skillId == active.SkillId ? GetProgramBindingId(active) :
            _contentRegistry.GetSkill(skillId).Program!.Triggers.Single(trigger =>
                trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.TuxingArmGameDamage)).Id;
        AdvanceEventRulesAndQueueFact(new ProgramGameDamageBonusArmedEvent(active.Id, skillId,
            bindingId, GetRuntimeSkillInstanceId(_players[ownerSeat], skillId), ownerSeat, amount));
    }

    // Shared consumption: every damage the armed seat deals carries the armed
    // amount, whatever the card kind and including chain propagation — the OL
    // text has no non-transferred-damage restriction.
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers, 40)]
    private IReadOnlyList<(CardUseEffectSource Source, int Amount)> GetArmedGameDamageBonuses(
        IDamageAttempt attack)
    {
        if (attack.IsSourceLess || !IsValidPlayerSeat(attack.SourceSeat) || !_players[attack.SourceSeat].IsAlive)
            return [];
        return CompleteProgramEventHistory().OfType<ProgramGameDamageBonusArmedEvent>()
            .Where(fact => fact.OwnerSeat == attack.SourceSeat)
            .Select(fact => (new CardUseEffectSource(fact.SkillId, fact.BindingId,
                fact.OwnerSeat, fact.SkillInstanceId), fact.Amount))
            .ToArray();
    }

    [ProgramCapability]
    private sealed class LiuHongProgramCapability(GameEngine engine) : ILiuHongProgramHost
    {
        public SkillProgramStepOutcome YujueResolve(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YujueProgramResolve(frame, effect);
        public SkillProgramStepOutcome ZhihuExpire(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhihuProgramExpire(frame, effect);
        public SkillProgramStepOutcome TuxingArmGameDamage(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.TuxingProgramArmGameDamage(frame, effect);
    }
}
