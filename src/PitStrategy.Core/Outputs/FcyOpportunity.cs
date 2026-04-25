namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// Active during a Full-Course Yellow / safety-car period when the pits are open.
    /// <see cref="TimeSavingsSeconds"/> is the delta vs. pitting under green flag.
    /// </summary>
    public sealed record FcyOpportunity(
        bool ShouldPitNow,
        double TimeSavingsSeconds,
        double FuelSavingsLitersIfShortFill,
        string Rationale);
}
