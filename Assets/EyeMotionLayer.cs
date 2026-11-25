using UnityEngine;

/// <summary>
/// Procedural "alive" layer for the eye puppet. EyeController_main computes expression targets for the brows and
/// eyelids, then adds the offsets produced here: slow brow/lid drift, random brow raises/furrows/squints,
/// lids and brows following the gaze, a brow dip on blinks, brow emphasis on stressed syllables while speaking
/// (dynamo/data/lipsync) and brow bumps on music beats (dynamo/data/fft).
/// All offsets are in Live2D parameter units; for eyelids, positive = more open.
/// Stepped explicitly by EyeController_main so its outputs are ready before the parameters are written.
/// </summary>
public class EyeMotionLayer : MonoBehaviour
{
    [Header("Global")]
    [Range(0f, 2f)] public float intensity = 1f;
    [Range(0f, 0.5f), Tooltip("Fraction of the brow range kept free at full-strength expressions (surprised, angry, mischievous pin the brows at ±1) so motion stays visible. 0 = expressions reach the extremes, as before.")]
    public float expressionHeadroom = 0.15f;

    [Header("Idle drift (slow noise)")]
    public float browDriftAmplitude = 0.08f;
    public float browAngleDriftAmplitude = 0.06f;
    [Tooltip("Lid drift only ever closes the lids a little, so it isn't lost to clamping at fully open.")]
    public float lidDriftAmplitude = 0.05f;
    public float driftSpeed = 0.25f;
    [Range(0f, 1f), Tooltip("0 = both brows drift together, 1 = fully independent.")]
    public float browAsymmetry = 0.3f;

    [Header("Idle gestures (random brow raises, furrows, squints)")]
    [Tooltip("Seconds between gestures (min, max).")]
    public Vector2 gestureInterval = new Vector2(3f, 12f);
    public float browRaiseAmplitude = 0.35f;
    public float furrowAmplitude = 0.2f;
    public float squintAmplitude = 0.15f;

    [Header("Gaze coupling")]
    [Tooltip("Upper lids lower when looking down (per unit of eyeball Y below 0).")]
    public float lidFollowsGazeDown = 0.25f;
    [Tooltip("Brows lift when looking up (per unit of eyeball Y above 0).")]
    public float browFollowsGazeUp = 0.15f;

    [Header("Blinks")]
    public float browDipOnBlink = 0.08f;
    [Range(0f, 1f)] public float doubleBlinkChance = 0.15f;

    [Header("Voice (dynamo/data/lipsync)")]
    [Tooltip("Continuous brow lift proportional to loudness.")]
    public float voiceBrowFollow = 0.12f;
    [Tooltip("Brow pulse on stressed syllables (downward furrow instead when angry/disgusted).")]
    public float emphasisAmplitude = 0.35f;
    [Tooltip("How far loudness must jump above its recent average to count as a stressed syllable.")]
    public float emphasisThreshold = 0.12f;
    public float emphasisCooldown = 0.3f;
    public float voiceLidWiden = 0.08f;
    [Tooltip("A/O widen the lids slightly, I/E narrow them slightly.")]
    public float vowelLidAmount = 0.06f;
    [Range(0f, 1f), Tooltip("Chance of a blink when a phrase of 1 s or more ends.")]
    public float blinkAtPauseChance = 0.5f;
    public float speakingBlinkRateMultiplier = 1.6f;
    public float speakingGazeActivity = 1.5f;

    [Header("Music (dynamo/data/fft)")]
    public bool musicReactive = true;
    public float beatBrowBump = 0.12f;
    public float musicLidWiden = 0.05f;
    public float minBeatInterval = 0.25f;

    // ---- outputs, read by EyeController_main ----
    public float BrowYL { get; private set; }
    public float BrowYR { get; private set; }
    public float BrowAngleL { get; private set; }
    public float BrowAngleR { get; private set; }
    public float LidL { get; private set; }
    public float LidR { get; private set; }
    /// <summary>Divide the next blink interval by this (people blink more while talking).</summary>
    public float BlinkRateMultiplier { get; private set; } = 1f;
    /// <summary>Divide the gaze micro-movement interval by this.</summary>
    public float GazeActivity { get; private set; } = 1f;

    /// <summary>Attack/hold/release envelope; retriggering while active keeps the larger target.</summary>
    struct Envelope
    {
        public float Value;
        float target, hold, attackTau, releaseTau;

        public void Trigger(float amp, float attack, float holdTime, float release)
        {
            target = hold > 0f ? (Mathf.Abs(amp) > Mathf.Abs(target) ? amp : target) : amp;
            hold = Mathf.Max(hold, attack + holdTime);
            attackTau = attack;
            releaseTau = release;
        }

        public float Step(float dt)
        {
            float goal = hold > 0f ? target : 0f;
            float tau = hold > 0f ? attackTau : releaseTau;
            Value += (goal - Value) * (1f - Mathf.Exp(-dt / Mathf.Max(tau, 0.001f)));
            hold -= dt;
            return Value;
        }
    }

    Envelope raiseL, raiseR, furrow, squint, emphasis, beat, blinkDip;
    float gestureTimer, emphasisTimer, beatTimer;
    float voiceFast, voiceSlow, vowelLid, musicEnergy;
    float speakingTime;
    bool wasSpeaking, wasBlinking, blinkRequested;
    int lastPeakCount;
    float seed;

    void Awake()
    {
        seed = Random.Range(0f, 1000f);
        gestureTimer = Random.Range(gestureInterval.x, gestureInterval.y);
        lastPeakCount = AudioFeed.MusicPeakCount;
    }

    /// <summary>True once after the layer wants a blink (end of a spoken phrase).</summary>
    public bool ConsumeBlinkRequest()
    {
        bool r = blinkRequested;
        blinkRequested = false;
        return r;
    }

    /// <param name="gazeY">Average eyeball Y parameter (positive = up).</param>
    /// <param name="blinking">Whether the lids are currently in a blink.</param>
    /// <param name="negativeMood">0..1, how angry/disgusted the current expression is.</param>
    public void Step(float dt, float gazeY, bool blinking, float negativeMood)
    {
        float t = Time.time * driftSpeed;

        // --- idle drift: shared component plus a per-brow component
        float sharedY = Noise(t, 0f), sharedA = Noise(t, 10f);
        float driftYL = Mathf.Lerp(sharedY, Noise(t, 20f), browAsymmetry) * browDriftAmplitude;
        float driftYR = Mathf.Lerp(sharedY, Noise(t, 30f), browAsymmetry) * browDriftAmplitude;
        float driftAL = Mathf.Lerp(sharedA, Noise(t, 40f), browAsymmetry) * browAngleDriftAmplitude;
        float driftAR = Mathf.Lerp(sharedA, Noise(t, 50f), browAsymmetry) * browAngleDriftAmplitude;
        float lidDrift = -Mathf.PerlinNoise(seed + t * 0.7f, 60f) * lidDriftAmplitude;

        // --- voice
        float level = AudioFeed.VoiceLevel;
        bool speaking = AudioFeed.Speaking;
        voiceFast = Smooth(voiceFast, level, 0.06f, dt);
        voiceSlow = Smooth(voiceSlow, level, 0.5f, dt);
        emphasisTimer -= dt;
        if (speaking && emphasisTimer <= 0f && voiceFast - voiceSlow > emphasisThreshold)
        {
            emphasis.Trigger(emphasisAmplitude * Mathf.Clamp01(0.4f + voiceFast), 0.06f, Random.Range(0.08f, 0.2f), 0.35f);
            emphasisTimer = emphasisCooldown * Random.Range(1f, 2f);
        }
        float vowelTarget = 0f;
        switch (AudioFeed.Vowel)
        {
            case 'A': case 'O': vowelTarget = vowelLidAmount; break;
            case 'I': case 'E': vowelTarget = -vowelLidAmount; break;
        }
        vowelLid = Smooth(vowelLid, vowelTarget, 0.12f, dt);

        if (speaking) { speakingTime += dt; }
        else
        {
            if (wasSpeaking && speakingTime > 1f && Random.value < blinkAtPauseChance) { blinkRequested = true; }
            speakingTime = 0f;
        }
        wasSpeaking = speaking;
        BlinkRateMultiplier = speaking ? speakingBlinkRateMultiplier : 1f;
        GazeActivity = speaking ? speakingGazeActivity : 1f;

        // --- music (ignored while speaking: desktop audio may contain our own voice)
        float music = musicReactive && !speaking ? AudioFeed.MusicEnergy : 0f;
        musicEnergy = Smooth(musicEnergy, music, 0.3f, dt);
        beatTimer -= dt;
        int peaks = AudioFeed.MusicPeakCount;
        if (peaks != lastPeakCount)
        {
            lastPeakCount = peaks;
            if (musicReactive && !speaking && beatTimer <= 0f)
            {
                beat.Trigger(beatBrowBump * (0.5f + 0.5f * musicEnergy), 0.04f, 0.05f, 0.2f);
                beatTimer = minBeatInterval;
            }
        }

        // --- idle gestures (speech emphasis replaces them while talking)
        gestureTimer -= dt;
        if (gestureTimer <= 0f)
        {
            if (!speaking) { TriggerGesture(); }
            gestureTimer = Random.Range(gestureInterval.x, gestureInterval.y);
        }

        // --- blink coupling
        if (blinking && !wasBlinking) { blinkDip.Trigger(browDipOnBlink, 0.04f, 0.08f, 0.15f); }
        wasBlinking = blinking;

        float rL = raiseL.Step(dt), rR = raiseR.Step(dt), f = furrow.Step(dt), sq = squint.Step(dt);
        float em = emphasis.Step(dt), bt = beat.Step(dt), dip = blinkDip.Step(dt);

        // Emphasis raises the brows, or furrows them when the expression is angry/disgusted
        float emY = em * (1f - 2f * negativeMood);
        float emA = em * negativeMood * 0.8f;
        float gazeBrow = Mathf.Max(0f, gazeY) * browFollowsGazeUp;
        float voiceBrow = voiceFast * voiceBrowFollow * (1f - negativeMood);
        float sharedBrowY = gazeBrow + voiceBrow + emY + bt - dip - f * 0.5f;

        BrowYL = intensity * (driftYL + rL + sharedBrowY);
        BrowYR = intensity * (driftYR + rR + sharedBrowY);
        BrowAngleL = intensity * (driftAL + f + emA);
        BrowAngleR = intensity * (driftAR + f + emA);

        float lid = lidDrift - sq - f * 0.3f
                  + Mathf.Min(0f, gazeY) * lidFollowsGazeDown
                  + voiceFast * voiceLidWiden + vowelLid
                  + musicEnergy * musicLidWiden;
        LidL = intensity * lid;
        LidR = intensity * lid;
    }

    void TriggerGesture()
    {
        float r = Random.value;
        if (r < 0.4f)
        {   // quick brow flash
            float a = browRaiseAmplitude * Random.Range(0.5f, 1f), hold = Random.Range(0.15f, 0.5f);
            raiseL.Trigger(a, 0.1f, hold, 0.35f);
            raiseR.Trigger(a, 0.1f, hold, 0.35f);
        }
        else if (r < 0.6f)
        {   // one brow
            float a = browRaiseAmplitude * Random.Range(0.4f, 0.8f), hold = Random.Range(0.4f, 1.2f);
            if (Random.value < 0.5f) { raiseL.Trigger(a, 0.15f, hold, 0.5f); }
            else { raiseR.Trigger(a, 0.15f, hold, 0.5f); }
        }
        else if (r < 0.8f)
        {   // brief furrow, as if concentrating
            furrow.Trigger(furrowAmplitude * Random.Range(0.5f, 1f), 0.25f, Random.Range(0.5f, 1.5f), 0.6f);
        }
        else
        {   // relaxed squint
            squint.Trigger(squintAmplitude * Random.Range(0.5f, 1f), 0.3f, Random.Range(0.5f, 1.5f), 0.5f);
        }
    }

    float Noise(float t, float row) => Mathf.PerlinNoise(seed + t, row) * 2f - 1f;

    static float Smooth(float current, float target, float tau, float dt) => current + (target - current) * (1f - Mathf.Exp(-dt / tau));
}
