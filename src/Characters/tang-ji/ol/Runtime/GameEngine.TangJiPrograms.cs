namespace CardGame.Core;

// 抗歌 evidence: the chosen 抗歌 target is public state (a 歌 marker on the
// target), so the event records both seats directly.
public sealed record ProgramKanggeChosenEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ChosenSeat) : IGameEvent;
// 抗歌 gain evidence: one record per drawn card batch; the per-turn cap ledger
// derives from these committed scalars, so no drawn card ids are needed.
public sealed record ProgramKanggeGainedDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int GainerSeat, int TurnNumber, int DrawnCount) : IGameEvent;
// 抗歌 rescue evidence: the once-per-round ledger derives from the round number.
public sealed record ProgramKanggeHealedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int VictimSeat, int RoundNumber) : IGameEvent;
// 抗歌 price evidence: the discarded cards are public through their movements;
// the event records only the count and the death that priced them.
public sealed record ProgramKanggeDeathPriceEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int VictimSeat, int DiscardedCardCount) : IGameEvent;
// 节烈 evidence: prevention, hp loss, chosen suit and the random gift count are
// all public facts; gifted card ids stay in their movement records.
public sealed record ProgramJieliePreventedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int DamageSourceSeat, int PreventedAmount, Suit ChosenSuit,
    int LostHp, int GiftedCount, int MarkedSeat) : IGameEvent;

// One 节烈 resolution: the chosen suit, marked seat and payment stages ride
// the owning program frame so the dying child (if the loss empties the owner's
// hp) resumes the same paid instruction and finishes the gift afterwards.
public sealed record JielieGiftState(Suit Suit, int MarkedSeat, bool Prevented, bool HpLost,
    int HpBeforePayment = 0, int PaidHpLost = 0);

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public JielieGiftState? JielieGift { get; init; }
}

public sealed partial class GameEngine
{

    private const int ProgramKanggeTurnDrawCap = 3;

    private static string SuitName(Suit suit) => suit switch
    {
        Suit.Spade => "黑桃", Suit.Heart => "红桃", Suit.Club => "梅花", _ => "方块"
    };

    [ProgramCapability]
    private sealed class TangJiProgramCapability(GameEngine engine) : ITangJiProgramHost
    {
        public SkillProgramStepOutcome KanggeChooseTarget(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.KanggeProgramChooseTarget(frame, effect);
        public SkillProgramStepOutcome KanggeGainDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.KanggeProgramGainDraw(frame, effect);
        public SkillProgramStepOutcome KanggeHealVictim(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.KanggeProgramHealVictim(frame, effect);
        public SkillProgramStepOutcome KanggeDeathPrice(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.KanggeProgramDeathPrice(frame, effect);
        public SkillProgramStepOutcome JieliePreventAndGift(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.JielieProgramPreventAndGift(frame, effect);
    }
}
