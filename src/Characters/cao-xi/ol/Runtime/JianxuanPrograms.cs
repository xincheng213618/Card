using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum CaoXiJianxuanStage { ChoosingRecipient, DrawChildren }
public sealed record CaoXiJianxuanReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentWindowId, long DamageFrameId, int ActualTurn, CaoXiJianxuanStage Stage = CaoXiJianxuanStage.ChoosingRecipient,
    int? RecipientSeat = null, int Cursor = 0, long Before = 0, long After = 0, int ActualDrawCount = 0);
public sealed record CaoXiJianxuanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long ParentWindowId, long DamageFrameId, int ActualTurn) : IGameEvent;
public sealed record CaoXiJianxuanRecipientEvent(long FrameId, int RecipientSeat) : IGameEvent;
public sealed record CaoXiJianxuanDrawPaidEvent(long FrameId, int Cursor, int RecipientSeat, int ActualDrawCount, long Before, long After) : IGameEvent;
public sealed record CaoXiJianxuanComparedEvent(long FrameId, int Cursor, int RecipientSeat, int HandCount, CaoXiNumbers Numbers, bool Repeat) : IGameEvent;
public sealed record CaoXiJianxuanCompletedEvent(long FrameId, int RecipientSeat, int DrawCalls) : IGameEvent;
internal sealed class ResolveJianxuanRepeatedDrawDescriptor() : CaoXiDescriptor(SkillProgramEffectOp.ResolveJianxuanRepeatedDraw)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]; }
