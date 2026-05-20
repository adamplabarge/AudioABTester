using AudioABTester.Audio;

namespace AudioABTester.Services;

public sealed class VolumeMatchService
{
    public float GetSuggestedGainDb(AudioTrack? referenceTrack, AudioTrack? comparisonTrack)
    {
        // TODO: Analyze RMS/LUFS and return a compensating gain value for the comparison track.
        // Keeping this service in the pipeline now makes it easier to add loudness-aware A/B/X later.
        _ = referenceTrack;
        _ = comparisonTrack;
        return 0f;
    }
}