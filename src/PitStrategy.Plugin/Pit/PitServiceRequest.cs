namespace PitStrategy.Core.Pit
{
    /// <summary>
    /// What services the player has currently armed for the next pit stop. Built from
    /// iRacing's <c>PitSvFuel</c>, <c>PitSvLFP/RFP/LRP/RRP</c>, and repair flags.
    /// </summary>
    public sealed record PitServiceRequest(
        bool RefuelArmed,
        double FuelToAddLiters,
        bool TireChangeArmed,
        bool RepairArmed = false,
        double RepairTimeSeconds = 0)
    {
        public static readonly PitServiceRequest None = new(
            RefuelArmed: false, FuelToAddLiters: 0, TireChangeArmed: false);
    }
}
