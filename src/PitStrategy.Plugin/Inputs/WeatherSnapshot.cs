namespace PitStrategy.Core.Inputs
{
    public sealed record WeatherSnapshot(
        double TrackTempC,
        double AirTempC,
        double Humidity,           // 0.0–1.0
        double SkiesCloudCover,    // 0.0 (clear) – 1.0 (overcast)
        double PrecipChance,       // 0.0–1.0
        bool IsRaining,
        bool TrackDeclaredWet)
    {
        public static readonly WeatherSnapshot Dry = new(
            TrackTempC: 25, AirTempC: 20, Humidity: 0.4,
            SkiesCloudCover: 0.1, PrecipChance: 0, IsRaining: false, TrackDeclaredWet: false);
    }
}
