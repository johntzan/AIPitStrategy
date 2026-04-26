namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// Projected state of one rival at the moment the player exits the pit lane.
    /// </summary>
    public sealed record RivalRejoinState(
        int CarIdx,
        int Position,
        double GapSeconds,        // signed: positive = rival is ahead of player on rejoin
        bool IsAhead,
        bool WillBePitting);
}
