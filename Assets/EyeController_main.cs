using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.PostProcessing;

public class EyeController_main : MonoBehaviour
{
    public Slider xSlider, ySlider, leftEyeClosenessSlider, rightEyeClosenessSlider, angrySlider, disgustedSlider, happySlider, neutralSlider, sadSlider, surprisedSlider, hypnoticSlider, heartSlider, rainbowSlider, nightmareSlider, gearsSlider, sansSlider, mischievousSlider, autoExposureSlider, analogGlitch, digitalGlitch;
    public Toggle sillyMode;
    public CubismParameter eyeballXL, eyeballYL, eyeballXR, eyeballYR, eyeOpenL, eyeOpenR, eyeSmileL, eyeSmileR, eyeDeform, browYL, browYR, browAngleL, browAngleR, activSpace, activCry, activHypno, activHeart, activRainbow, activNightmare, activGears, activFire, activSans;
    public CubismRenderer mask_esclera1, mask_esclera2;
    public GameObject[] normalBlinkSounds;
    public GameObject[] sillyBlinkSounds;
    public GameObject gearBlinkSound;
    public PostProcessProfile postProcessProfile;
    [Tooltip("Procedural brow/lid motion and audio reactions. Added automatically at runtime if missing.")]
    public EyeMotionLayer motion;
    AutoExposure autoExposure;

    const float SPEED = 18;
    const float TIMER_IDLE_RAND_MAX = 1.5f;
    const float TIMER_BLINK_RAND_MAX = 16.0f;
    const float TIMER_GLITCH_RAND_MAX = 60.0f;
    const float TIMER_GEARS_RAND_MAX = 60.0f;
    const float BLINK_DURATION = 0.1f;
    const float AVG_SLIDER = 0.5f;
    static double[] X_SET = { -1, -0.9, -0.8, -0.7, -0.6, -0.5, -0.4, -0.3, -0.2, -0.1, 0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1 };
    static double[] Y_SET = { -1, -0.9, -0.8, -0.7, -0.6, -0.5, -0.4, -0.3, -0.2, -0.1, 0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1 };
    double X_DELTA_LIMIT = 2.0 / (X_SET.Length - 1) * 0.75;
    double Y_DELTA_LIMIT = 2.0 / (Y_SET.Length - 1) * 0.75;

    float xCurrent = 0;
    float yCurrent = 0;
    static float timer_idle = 0;
    static float timer_blink = 0;
    static float timer_glitch = 0;
    static float timer_gears = 0;
    static float glitchAnimationTimer = 0;
    static bool isGlitchAnimating = false;
    static bool useAnalogGlitch = false;
    static float offsetX_idle = 0;
    static float offsetY_idle = 0;
    static bool lastBlinkWasDouble = false;

    // Expression-driven values before the procedural motion offsets are added
    bool baseInitialized = false;
    float baseOpenL, baseOpenR, baseBrowYL, baseBrowAngleL;

    private Func<double, double[], double> closest = (val, set) =>
    {
        var c = set.Select(i => Math.Abs(val - i)).ToArray();
        return set[Array.IndexOf(c, c.Min())];
    };

    private static float RandomGaussian(float minValue = 0.0f, float maxValue = 1.0f)
    {
        float u, v, S;
        do
        {
            u = 2.0f * UnityEngine.Random.value - 1.0f;
            v = 2.0f * UnityEngine.Random.value - 1.0f;
            S = u * u + v * v;
        }
        while (S >= 1.0f);
        float std = u * Mathf.Sqrt(-2.0f * Mathf.Log(S) / S);
        float mean = (minValue + maxValue) / 2.0f;
        float sigma = (maxValue - mean) / 3.0f;
        return Mathf.Clamp(std * sigma + mean, minValue, maxValue);
    }

    private void MoveEyes()
    {
        if (sillyMode.isOn)
        {
            eyeballXL.Value = Mathf.Lerp(eyeballXL.Value, 0.7f, Time.deltaTime * SPEED);
            eyeballXR.Value = Mathf.Lerp(eyeballXR.Value, -0.9f, Time.deltaTime * SPEED);
            eyeballYL.Value = Mathf.Lerp(eyeballYL.Value, 0.7f, Time.deltaTime * SPEED);
            eyeballYR.Value = Mathf.Lerp(eyeballYR.Value, -0.8f, Time.deltaTime * SPEED);
            return;
        }
        if (hypnoticSlider.value > AVG_SLIDER)
        {
            eyeballXL.Value = Mathf.Lerp(eyeballXL.Value, 0, Time.deltaTime * SPEED);
            eyeballXR.Value = Mathf.Lerp(eyeballXR.Value, 0, Time.deltaTime * SPEED);
            eyeballYL.Value = Mathf.Lerp(eyeballYL.Value, 0, Time.deltaTime * SPEED);
            eyeballYR.Value = Mathf.Lerp(eyeballYR.Value, 0, Time.deltaTime * SPEED);
            return;
        }
        if (Math.Abs(xSlider.value - xCurrent) > X_DELTA_LIMIT) { 
            xCurrent = (float)closest(xSlider.value, X_SET);
            timer_idle = UnityEngine.Random.Range(0.0f, TIMER_IDLE_RAND_MAX);
        }
        if (Math.Abs(ySlider.value - yCurrent) > Y_DELTA_LIMIT) { 
            yCurrent = (float)closest(ySlider.value, Y_SET);
            timer_idle = UnityEngine.Random.Range(0.0f, TIMER_IDLE_RAND_MAX);
        }
        if (timer_idle <= 0)
        {
            offsetX_idle = UnityEngine.Random.Range((float)-xSlider.maxValue / 20, (float)xSlider.maxValue / 20);
            offsetY_idle = UnityEngine.Random.Range((float)-ySlider.maxValue / 20, (float)ySlider.maxValue / 20);
            timer_idle = UnityEngine.Random.Range(0.0f, TIMER_IDLE_RAND_MAX / motion.GazeActivity);
        }
        else { timer_idle -= Time.deltaTime; }
        if (Math.Abs(xCurrent) < AVG_SLIDER/2)
        {
            eyeballXL.Value = Mathf.Lerp(eyeballXL.Value, xCurrent + offsetX_idle - 0.2f, Time.deltaTime * SPEED);
            eyeballXR.Value = Mathf.Lerp(eyeballXR.Value, xCurrent + offsetX_idle + 0.2f, Time.deltaTime * SPEED);
        }
        else 
        {
            eyeballXL.Value = Mathf.Lerp(eyeballXL.Value, xCurrent + offsetX_idle, Time.deltaTime * SPEED);
            eyeballXR.Value = Mathf.Lerp(eyeballXR.Value, xCurrent + offsetX_idle, Time.deltaTime * SPEED);
        }
        eyeballYL.Value = Mathf.Lerp(eyeballYL.Value, yCurrent + offsetY_idle, Time.deltaTime * SPEED);
        eyeballYR.Value = Mathf.Lerp(eyeballYR.Value, yCurrent + offsetY_idle, Time.deltaTime * SPEED);
    }

    private void MoveEyelids()
    {
        float targetL, targetR;
        bool lidsLocked = true;
        if (hypnoticSlider.value > AVG_SLIDER)
        {
            targetL = leftEyeClosenessSlider.maxValue - 0.2f;
            targetR = rightEyeClosenessSlider.maxValue - 0.2f;
        }
        else if (mischievousSlider.value > AVG_SLIDER)
        {
            targetL = leftEyeClosenessSlider.maxValue - 0.7f;
            targetR = rightEyeClosenessSlider.maxValue - 0.7f;
        }
        else
        {
            lidsLocked = false;
            if (motion.ConsumeBlinkRequest() && timer_blink > 0.15f) { timer_blink = 0; }
            if (timer_blink <= 0)
            {
                targetL = leftEyeClosenessSlider.minValue;
                targetR = rightEyeClosenessSlider.minValue;
                if (timer_blink < -BLINK_DURATION) {
                    GameObject selectedBlinkSound = null;
                    if (activGears.Value > AVG_SLIDER) { selectedBlinkSound = gearBlinkSound; }
                    else if (sillyMode.isOn && sillyBlinkSounds.Length > 0) { selectedBlinkSound = sillyBlinkSounds[UnityEngine.Random.Range(0, sillyBlinkSounds.Length)]; }
                    else if (normalBlinkSounds.Length > 0) { selectedBlinkSound = normalBlinkSounds[UnityEngine.Random.Range(0, normalBlinkSounds.Length)]; }
                    if (selectedBlinkSound != null) {
                        selectedBlinkSound.SetActive(false);
                        selectedBlinkSound.SetActive(true);
                    }
                    if (!lastBlinkWasDouble && UnityEngine.Random.value < motion.doubleBlinkChance)
                    {
                        timer_blink = UnityEngine.Random.Range(0.2f, 0.35f);
                        lastBlinkWasDouble = true;
                    }
                    else
                    {
                        timer_blink = RandomGaussian(1.0f, TIMER_BLINK_RAND_MAX) / motion.BlinkRateMultiplier;
                        lastBlinkWasDouble = false;
                    }
                }
            }
            else
            {
                targetL = leftEyeClosenessSlider.maxValue - leftEyeClosenessSlider.value;
                targetR = rightEyeClosenessSlider.maxValue - rightEyeClosenessSlider.value;
            }
            timer_blink -= Time.deltaTime;
        }
        baseOpenL = Mathf.Lerp(baseOpenL, targetL, Time.deltaTime * SPEED);
        baseOpenR = Mathf.Lerp(baseOpenR, targetR, Time.deltaTime * SPEED);
        eyeOpenL.Value = ComposeLid(eyeOpenL, baseOpenL, lidsLocked ? 0 : motion.LidL);
        eyeOpenR.Value = ComposeLid(eyeOpenR, baseOpenR, lidsLocked ? 0 : motion.LidR);
    }

    // Lid offsets fade out as the lid closes, so blinks still close fully
    private static float ComposeLid(CubismParameter lid, float baseValue, float offset)
    {
        float open01 = Mathf.InverseLerp(lid.MinimumValue, lid.MaximumValue, baseValue);
        return Mathf.Clamp(baseValue + offset * open01, lid.MinimumValue, lid.MaximumValue);
    }

    // Brow expressions are scaled back by the motion layer's headroom so full-strength poses (score 1.0 pins the
    // brows at ±1) still leave room for the procedural motion instead of clamping it away
    private float ComposeBrow(CubismParameter param, float baseValue, float offset)
    {
        return Mathf.Clamp(baseValue * (1f - motion.expressionHeadroom) + offset, param.MinimumValue, param.MaximumValue);
    }

    private void ApplyExpressions()
    {
        float topExpr = Mathf.Max(angrySlider.value, disgustedSlider.value, happySlider.value, neutralSlider.value, sadSlider.value, surprisedSlider.value);
        //Smile L
        if (topExpr == happySlider.value || topExpr == disgustedSlider.value || topExpr == angrySlider.value) { eyeSmileL.Value = Mathf.Lerp(eyeSmileL.Value, topExpr, Time.deltaTime * SPEED); }
        else { eyeSmileL.Value = Mathf.Lerp(eyeSmileL.Value, 0, Time.deltaTime * SPEED); }
        //Smile R
        if (topExpr == happySlider.value) { eyeSmileR.Value = Mathf.Lerp(eyeSmileR.Value, topExpr, Time.deltaTime * SPEED); }
        else { eyeSmileR.Value = Mathf.Lerp(eyeSmileR.Value, 0, Time.deltaTime * SPEED); }
        //Deform
        if (sillyMode.isOn) { eyeDeform.Value = Mathf.Lerp(eyeDeform.Value, 0, Time.deltaTime * SPEED); }
        else if (topExpr == surprisedSlider.value || topExpr == disgustedSlider.value) { eyeDeform.Value = Mathf.Lerp(eyeDeform.Value, topExpr*0.5f, Time.deltaTime * SPEED); }
        else if (topExpr == angrySlider.value) { eyeDeform.Value = Mathf.Lerp(eyeDeform.Value, topExpr*0.25f, Time.deltaTime * SPEED); }
        else { eyeDeform.Value = Mathf.Lerp(eyeDeform.Value, 0, Time.deltaTime * SPEED); }
        //Brow Y L
        if (mischievousSlider.value > AVG_SLIDER) { baseBrowYL = Mathf.Lerp(baseBrowYL, -browYL.MaximumValue, Time.deltaTime * SPEED); }
        else if (topExpr == surprisedSlider.value) { baseBrowYL = Mathf.Lerp(baseBrowYL, topExpr, Time.deltaTime * SPEED); }
        else if (topExpr == sadSlider.value || topExpr == angrySlider.value || topExpr == disgustedSlider.value) { baseBrowYL = Mathf.Lerp(baseBrowYL, -topExpr, Time.deltaTime * SPEED); }
        else { baseBrowYL = Mathf.Lerp(baseBrowYL, 0, Time.deltaTime * SPEED); }
        //Brow Y R
        float baseBrowYR = topExpr == disgustedSlider.value ? -baseBrowYL : baseBrowYL;
        //Brow Angle L
        if (mischievousSlider.value > AVG_SLIDER) { baseBrowAngleL = Mathf.Lerp(baseBrowAngleL, browAngleL.MaximumValue - 0.5f, Time.deltaTime * SPEED); }
        else if (topExpr == angrySlider.value || topExpr == disgustedSlider.value) { baseBrowAngleL = Mathf.Lerp(baseBrowAngleL, topExpr, Time.deltaTime * SPEED); }
        else if (topExpr == sadSlider.value) { baseBrowAngleL = Mathf.Lerp(baseBrowAngleL, -topExpr, Time.deltaTime * SPEED); }
        else { baseBrowAngleL = Mathf.Lerp(baseBrowAngleL, 0, Time.deltaTime * SPEED); }
        //Brow Angle R
        float baseBrowAngleR = topExpr == disgustedSlider.value ? baseBrowAngleL * 0.75f : baseBrowAngleL;
        //Brows = expression + procedural motion
        browYL.Value = ComposeBrow(browYL, baseBrowYL, motion.BrowYL);
        browYR.Value = ComposeBrow(browYR, baseBrowYR, motion.BrowYR);
        browAngleL.Value = ComposeBrow(browAngleL, baseBrowAngleL, motion.BrowAngleL);
        browAngleR.Value = ComposeBrow(browAngleR, baseBrowAngleR, motion.BrowAngleR);
        //Cry
        if (topExpr == sadSlider.value) { activCry.Value = Mathf.Lerp(activCry.Value, topExpr, Time.deltaTime * SPEED); }
        else { activCry.Value = Mathf.Lerp(activCry.Value, 0, Time.deltaTime * SPEED); }
        //Fire
        if (topExpr == angrySlider.value) { activFire.Value = Mathf.Lerp(activFire.Value, topExpr * 0f, Time.deltaTime * SPEED); }
        else { activFire.Value = Mathf.Lerp(activFire.Value, 0, Time.deltaTime * SPEED); }
        //Others
        activSpace.Value = eyeSmileR.Value;
        activHypno.Value = Mathf.Lerp(activHypno.Value, hypnoticSlider.value, Time.deltaTime * SPEED);
        activHeart.Value = Mathf.Lerp(activHeart.Value, heartSlider.value, Time.deltaTime * SPEED);
        activRainbow.Value = Mathf.Lerp(activRainbow.Value, rainbowSlider.value, Time.deltaTime * SPEED);
        activNightmare.Value = Mathf.Lerp(activNightmare.Value, nightmareSlider.value, Time.deltaTime * SPEED);
        activGears.Value = Mathf.Lerp(activGears.Value, gearsSlider.value, Time.deltaTime * SPEED);
        activSans.Value = Mathf.Lerp(activSans.Value, sansSlider.value, Time.deltaTime * SPEED);
        //Sclera masks
        if (activNightmare.Value > AVG_SLIDER || activGears.Value > AVG_SLIDER || activSans.Value > AVG_SLIDER) { mask_esclera1.LocalSortingOrder = -1; mask_esclera2.LocalSortingOrder = -1; }
        else { mask_esclera1.LocalSortingOrder = 0; mask_esclera2.LocalSortingOrder = 0; }
    }

    private void UpdateAutoExposure()
    {
        autoExposure.keyValue.value = autoExposureSlider.value;
    }

    private void GlitchEffect()
    {
        if (isGlitchAnimating)
        {
            float progress = glitchAnimationTimer * SPEED / 60.0f;
            if (progress < 1f)
            {
                if (useAnalogGlitch)
                {
                    if (progress <= 0.5f) { analogGlitch.value = Mathf.Lerp(0f, 1f, progress * 2f); }
                    else { analogGlitch.value = Mathf.Lerp(1f, 0f, (progress - 0.5f) * 2f); }
                }
                else
                {
                    if (progress <= 0.5f) { digitalGlitch.value = Mathf.Lerp(0f, 0.5f, progress * 2f); }
                    else { digitalGlitch.value = Mathf.Lerp(0.5f, 0f, (progress - 0.5f) * 2f); }
                }
                glitchAnimationTimer += Time.deltaTime;
            }
            else
            {
                isGlitchAnimating = false;
                analogGlitch.value = 0f;
                digitalGlitch.value = 0f;
                timer_glitch = RandomGaussian(1.0f, TIMER_GLITCH_RAND_MAX);
            }
        }
        else if (timer_glitch <= 0)
        {
            isGlitchAnimating = true;
            glitchAnimationTimer = 0f;
            useAnalogGlitch = UnityEngine.Random.Range(0, 2) == 0;
        }
        else { timer_glitch -= Time.deltaTime; }
    }

    private void Start()
    {
        autoExposure = postProcessProfile.GetSetting<AutoExposure>();
        if (motion == null) { motion = GetComponent<EyeMotionLayer>(); }
        if (motion == null) { motion = gameObject.AddComponent<EyeMotionLayer>(); }
    }

    private void StepMotionLayer()
    {
        if (!baseInitialized)
        {
            baseOpenL = eyeOpenL.Value; baseOpenR = eyeOpenR.Value;
            baseBrowYL = browYL.Value; baseBrowAngleL = browAngleL.Value;
            baseInitialized = true;
        }
        float topExpr = Mathf.Max(angrySlider.value, disgustedSlider.value, happySlider.value, neutralSlider.value, sadSlider.value, surprisedSlider.value);
        float negativeMood = (topExpr == angrySlider.value || topExpr == disgustedSlider.value) ? Mathf.Clamp01(topExpr) : 0f;
        float gazeY = (eyeballYL.Value + eyeballYR.Value) * 0.5f;
        motion.Step(Time.deltaTime, gazeY, timer_blink <= 0, negativeMood);
    }

    private void Update()
    {
        StepMotionLayer();
        MoveEyes();
        MoveEyelids();
        ApplyExpressions();
        UpdateAutoExposure();
        GlitchEffect();
    }
}
