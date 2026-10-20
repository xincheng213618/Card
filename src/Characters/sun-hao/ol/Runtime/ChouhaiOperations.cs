using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record SunHaoChouhaiFrozenEvent(long AttackId, int TargetSeat, int Cursor, int InheritedAmount, int Bonus, long InvoiceId = 0) : IGameEvent;
public sealed record SunHaoChouhaiRebasedEvent(long AttackId, int TargetSeat, int Cursor, int InheritedAmount) : IGameEvent;
public sealed record SunHaoChouhaiTransferredEvent(long AttackId, int OriginalSeat, int TargetSeat, int Cursor, long WindowId) : IGameEvent;
public sealed record SunHaoChouhaiReceipt(long AttackId, int TargetSeat, int Cursor, int InheritedAmount, int Bonus,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? TransferredTarget = null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? TransferWindowId = null, long InvoiceId = 0);
