using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaoYingGuessResolvedEvent(long ProgramFrameId, CardConversionSource Source,
    int TurnNumber, int TurnOwner, long UseId, long? ActionId, int TargetSeat, int CorrectCount) : IGameEvent;
public sealed record CaoYingDamageConsumedEvent(long ProgramFrameId, long DamageFrameId) : IGameEvent;
public sealed record CaoYingGrantExpiredEvent(int OwnerSeat, string GrantId) : IGameEvent;
internal sealed class GuessTargetCategoriesAndArmTurnDamageDescriptor : CaoYingDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage; }
