using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum SunRuOlWeimianStage { Slots, CostChildren, Benefits, Applying, RestoreSlot, RestoreChildren, RecoveryChildren, DiscardChildren, DrawChildren }
public sealed record SunRuOlWeimianReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    SunRuOlTurnToken Turn, int RecipientSeat, SunRuOlWeimianStage Stage, int SlotsMask = 0, int BenefitsMask = 0,
    int NextBenefit = 1, bool Paid = false, int SequenceBefore = 0, int SequenceAfter = 0);
public sealed record SunRuOlWeimianPaidEvent(long FrameId, CardConversionSource Source, int RecipientSeat,
    SunRuOlTurnToken Turn, int SlotsMask, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record SunRuOlWeimianBenefitsChosenEvent(long FrameId, int RecipientSeat, int BenefitsMask) : IGameEvent;
public sealed record SunRuOlWeimianBenefitIssuedEvent(long FrameId, int RecipientSeat, int Benefit,
    EquipmentSlot? RestoredSlot, int SequenceBefore, int SequenceAfter) : IGameEvent;
