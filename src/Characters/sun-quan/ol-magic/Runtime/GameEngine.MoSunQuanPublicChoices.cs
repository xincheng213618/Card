namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<PlayerMarkerSnapshot>? AppendMoQuanyuPublicMarkers(int participant,
        IReadOnlyList<PlayerMarkerSnapshot>? ordinary)
    {
        var choices = GetMoQuanyuPublicChoices().Where(c => c.ParticipantSeat == participant && c.HistoryMask != 0)
            .Select(c => new PlayerMarkerSnapshot(PlayerMarkerKind.MoQuanyuChoice,
                $"权御（{c.IssuerSeat + 1}号位）：{MoQuanyuEffectName(c.CurrentChoice)}；历次：" +
                string.Join("、", Enum.GetValues<MoQuanyuEffect>().Where(e => e != MoQuanyuEffect.None &&
                    (c.HistoryMask & MoQuanyuBit(e)) != 0).Select(MoQuanyuEffectName)), 1)).ToArray();
        // Only already revealed facts are projected. Pending votes never leave
        // their owning private program frame or enter these public badges.
        return choices.Length == 0 ? ordinary : Array.AsReadOnly((ordinary ?? []).Concat(choices)
            .OrderBy(marker => marker.Kind).ThenBy(marker => marker.Name, StringComparer.Ordinal).ToArray());
    }
}
