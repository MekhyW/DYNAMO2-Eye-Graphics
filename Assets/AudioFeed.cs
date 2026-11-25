using UnityEngine;

/// <summary>
/// Latest audio features forwarded by the DYNAMO core's unity node ("AUDIO ..." messages, see Websocket).
/// Voice comes from dynamo/data/lipsync (microphone node), music from dynamo/data/fft (desktop audio node).
/// Written and read on the main thread only.
/// </summary>
public static class AudioFeed
{
    /// <summary>Seconds without an AUDIO message before everything reads as silent.</summary>
    public const float STALE_AFTER = 0.5f;

    static float voiceLevel, musicEnergy;
    static bool speaking;
    static char vowel = '-';
    static float lastUpdate = float.NegativeInfinity;

    static bool Fresh => Time.unscaledTime - lastUpdate < STALE_AFTER;

    /// <summary>Mic loudness 0..1 (peak-held between messages).</summary>
    public static float VoiceLevel => Fresh ? voiceLevel : 0f;
    public static bool Speaking => Fresh && speaking;
    /// <summary>'A', 'I', 'U', 'E', 'O', or '-' when no vowel is being spoken.</summary>
    public static char Vowel => Speaking ? vowel : '-';
    /// <summary>Desktop audio energy 0..1.</summary>
    public static float MusicEnergy => Fresh ? musicEnergy : 0f;
    /// <summary>Increments once per beat/transient reported by the FFT node; compare against a stored value to consume.</summary>
    public static int MusicPeakCount { get; private set; }

    public static void Set(float level, bool isSpeaking, char vowelChar, float energy, bool peak)
    {
        voiceLevel = Mathf.Clamp01(level);
        speaking = isSpeaking;
        vowel = char.ToUpperInvariant(vowelChar);
        musicEnergy = Mathf.Clamp01(energy);
        if (peak) { MusicPeakCount++; }
        lastUpdate = Time.unscaledTime;
    }
}
