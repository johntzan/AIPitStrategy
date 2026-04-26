namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// Progress reporter payload for long-running Monte Carlo runs (Phase 3).
    /// </summary>
    public sealed record SimProgress(int CompletedTrials, int TotalTrials, string CandidateName)
    {
        public double Fraction => TotalTrials <= 0 ? 0 : (double)CompletedTrials / TotalTrials;
    }
}
