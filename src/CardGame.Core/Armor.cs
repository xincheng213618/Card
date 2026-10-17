using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed partial class CharacterState
{
    /// <summary>Damage payment resource, independent of equipment and HP.</summary>
    public int Armor { get; internal set; }
}

public sealed partial record PlayerSnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Armor { get; init; }
}

public sealed record ArmorGrantedEvent(long ProgramFrameId, int InstructionIndex, CardUseEffectSource Source,
    int PlayerSeat, int Amount, int ArmorBefore, int ArmorAfter) : IGameEvent;
