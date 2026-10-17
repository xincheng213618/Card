using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.Audio;

public enum GameSound { Card, Response, Hit, Fire, Thunder, Recover, YourTurn, Prompt, Dying, Death, Victory, Defeat, Draw, StoneAxe, Qinglong }

public sealed class GameSoundsEventArgs(IEnumerable<GameSound> sounds, IEnumerable<GameSound>? notificationSounds = null) : EventArgs
{
    public IReadOnlyList<GameSound> Sounds { get; } = Array.AsReadOnly(sounds.ToArray());
    public IReadOnlyList<GameSound> NotificationSounds { get; } = Array.AsReadOnly(notificationSounds?.ToArray() ?? []);
}

public static class GameSoundRules
{
    public static GameSound Outcome(GameSnapshot view)
    {
        if (view.Winner == Winner.RankedTeams)
            return view.WinnerTeamId is { } team && view.Players.FirstOrDefault(player => player.IsHuman)?.TeamId == team
                ? GameSound.Victory : GameSound.Defeat;
        if (view.Winner == Winner.SoleSurvivor)
            return view.WinnerSeat is { } seat && view.Players.FirstOrDefault(player => player.IsHuman)?.Seat == seat
                ? GameSound.Victory : GameSound.Defeat;
        if (view.ModeKind == ContentModeKind.NationalWarLite && view.Winner is not (Winner.Draw or Winner.None))
            return view.WinnerFactionId is { } faction && view.Players.FirstOrDefault(player => player.IsHuman)?.FactionId == faction
                ? GameSound.Victory : GameSound.Defeat;
        return Outcome(view.Winner, view.Players.FirstOrDefault(player => player.IsHuman)?.Role);
    }
    public static IReadOnlyList<GameSound> FromPublicCues(IEnumerable<BattleCue> cues) => cues
        .Select(cue => cue.Kind switch
        {
            BattleCueKind.Card => GameSound.Card,
            BattleCueKind.Judgment when cue.HasJudgmentCard => GameSound.Card,
            BattleCueKind.Draw when cue.HasDrawCards => GameSound.Card,
            BattleCueKind.Response or BattleCueKind.Skill => GameSound.Response,
            BattleCueKind.Weapon when !cue.IsInquiry && cue.IsWeaponActivated => cue.WeaponKind switch
            {
                CardKind.StoneAxe => GameSound.StoneAxe,
                CardKind.QinglongCrescentBlade => GameSound.Qinglong,
                _ => (GameSound?)null
            },
            BattleCueKind.Damage => cue.Nature switch { DamageNature.Fire => GameSound.Fire, DamageNature.Thunder => GameSound.Thunder, _ => GameSound.Hit },
            BattleCueKind.Recovery => GameSound.Recover,
            BattleCueKind.Dying => GameSound.Dying,
            BattleCueKind.Death => GameSound.Death,
            _ => (GameSound?)null
        }).Where(sound => sound.HasValue).Select(sound => sound!.Value).Distinct().ToArray();

    public static GameSound Outcome(Winner winner, Role? playerRole) => winner switch
    {
        Winner.Draw or Winner.None => GameSound.Draw,
        Winner.LordAndLoyalists when playerRole is Role.Lord or Role.Loyalist => GameSound.Victory,
        Winner.Rebels when playerRole is Role.Rebel => GameSound.Victory,
        Winner.Renegade when playerRole is Role.Renegade => GameSound.Victory,
        Winner.TeamA when playerRole == Role.TeamA => GameSound.Victory,
        Winner.TeamB when playerRole == Role.TeamB => GameSound.Victory,
        _ => GameSound.Defeat
    };

    public static int Priority(GameSound sound) => sound switch
    {
        GameSound.Victory or GameSound.Defeat or GameSound.Draw => 100,
        GameSound.Dying or GameSound.Death => 90,
        GameSound.YourTurn or GameSound.Prompt => 80,
        GameSound.Hit or GameSound.Fire or GameSound.Thunder => 60,
        GameSound.Recover => 50,
        GameSound.StoneAxe or GameSound.Qinglong => 40,
        GameSound.Response => 30,
        _ => 10
    };

    public static IReadOnlyList<GameSound> SelectBatch(IEnumerable<GameSound> sounds)
    {
        var ordered = sounds.Distinct().OrderByDescending(Priority).ToArray();
        // A result replaces the preceding fight; ordinary actions layer at most two short cues.
        return ordered.Take(ordered.Any(sound => Priority(sound) == 100) ? 1 : 2).Reverse().ToArray();
    }
}
