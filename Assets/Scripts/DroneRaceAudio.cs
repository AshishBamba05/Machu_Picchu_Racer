using UnityEngine;

[DisallowMultipleComponent]
public class DroneRaceAudio : MonoBehaviour
{
    private const int SampleRate = 44100;
    private const float EngineClipLengthSeconds = 2f;

    [Header("Tracking")]
    [SerializeField] private Transform target;

    [Header("Engine")]
    [SerializeField] private float minAudibleSpeed = 0.02f;
    [SerializeField] private float maxExpectedSpeed = 2.6f;
    [SerializeField] private float engineBasePitch = 0.72f;
    [SerializeField] private float enginePitchRange = 1.05f;
    [SerializeField] private float engineBaseVolume = 0.04f;
    [SerializeField] private float engineVolumeRange = 0.2f;
    [SerializeField] private float engineResponsiveness = 6f;

    private AudioSource engineSource;
    private AudioSource sfxSource;
    private Vector3 lastPosition;
    private float smoothedSpeed;
    private bool engineActive;
    private AudioClip engineClip;
    private AudioClip checkpointClip;
    private AudioClip crashClip;
    private AudioClip finishClip;

    public void Initialize(Transform targetTransform)
    {
        target = targetTransform;
        lastPosition = target != null ? target.position : transform.position;
        EnsureAudioSources();
        EnsureClips();
        if (engineSource != null && !engineSource.isPlaying)
        {
            engineSource.Play();
        }
    }

    public void SetEngineActive(bool isActive)
    {
        engineActive = isActive;
    }

    public void PlayCountdownTick(int secondsRemaining)
    {
        var pitch = Mathf.Clamp01((secondsRemaining - 1) / 3f);
        var clip = CreateToneClip(
            $"Countdown Tick {secondsRemaining}",
            0.18f,
            Mathf.Lerp(540f, 760f, 1f - pitch),
            0.14f,
            waveSharpness: 0.2f,
            vibratoAmount: 0.01f,
            vibratoFrequency: 10f);
        sfxSource.PlayOneShot(clip, 0.9f);
    }

    public void PlayCountdownGo()
    {
        var clip = CreateDualToneClip("Countdown Go", 0.34f, 740f, 1100f, 0.16f);
        sfxSource.PlayOneShot(clip, 1f);
    }

    public void PlayCheckpoint()
    {
        sfxSource.PlayOneShot(checkpointClip, 0.95f);
    }

    public void PlayCrash()
    {
        sfxSource.PlayOneShot(crashClip, 1f);
    }

    public void PlayFinish()
    {
        sfxSource.PlayOneShot(finishClip, 1f);
    }

    private void Awake()
    {
        EnsureAudioSources();
        EnsureClips();
        lastPosition = target != null ? target.position : transform.position;
    }

    private void Update()
    {
        if (target == null)
        {
            return;
        }

        var deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        var measuredSpeed = Vector3.Distance(target.position, lastPosition) / deltaTime;
        lastPosition = target.position;

        smoothedSpeed = Mathf.Lerp(smoothedSpeed, measuredSpeed, 1f - Mathf.Exp(-engineResponsiveness * deltaTime));

        var normalizedSpeed = Mathf.InverseLerp(minAudibleSpeed, maxExpectedSpeed, smoothedSpeed);
        var targetVolume = engineActive ? engineBaseVolume + (normalizedSpeed * engineVolumeRange) : 0f;
        var targetPitch = engineBasePitch + (normalizedSpeed * enginePitchRange);

        engineSource.volume = Mathf.Lerp(engineSource.volume, targetVolume, 1f - Mathf.Exp(-8f * deltaTime));
        engineSource.pitch = Mathf.Lerp(engineSource.pitch, targetPitch, 1f - Mathf.Exp(-10f * deltaTime));
    }

    private void EnsureAudioSources()
    {
        if (engineSource == null)
        {
            engineSource = gameObject.AddComponent<AudioSource>();
            engineSource.playOnAwake = false;
            engineSource.loop = true;
            engineSource.spatialBlend = 0f;
            engineSource.volume = 0f;
            engineSource.pitch = engineBasePitch;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.volume = 1f;
        }
    }

    private void EnsureClips()
    {
        engineClip ??= CreateEngineLoopClip();
        checkpointClip ??= CreateDualToneClip("Checkpoint", 0.28f, 880f, 1320f, 0.15f);
        crashClip ??= CreateNoiseBurstClip("Crash", 0.55f, 0.28f, 160f);
        finishClip ??= CreateFinishClip();

        engineSource.clip = engineClip;
    }

    private static AudioClip CreateEngineLoopClip()
    {
        var sampleCount = Mathf.CeilToInt(SampleRate * EngineClipLengthSeconds);
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)SampleRate;
            var fundamental = Mathf.Sin(2f * Mathf.PI * 82f * t);
            var harmonicA = Mathf.Sin(2f * Mathf.PI * 164f * t + 0.35f);
            var harmonicB = Mathf.Sin(2f * Mathf.PI * 246f * t + 0.9f);
            var tremolo = 0.72f + (0.28f * Mathf.Sin(2f * Mathf.PI * 6f * t));
            samples[i] = (fundamental * 0.52f + harmonicA * 0.24f + harmonicB * 0.14f) * tremolo;
        }

        return CreateClipFromSamples("Engine Loop", samples);
    }

    private static AudioClip CreateDualToneClip(string clipName, float durationSeconds, float firstFrequency, float secondFrequency, float amplitude)
    {
        var sampleCount = Mathf.CeilToInt(SampleRate * durationSeconds);
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)SampleRate;
            var envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(i / (float)(sampleCount - 1)));
            var toneA = Mathf.Sin(2f * Mathf.PI * firstFrequency * t);
            var toneB = Mathf.Sin(2f * Mathf.PI * secondFrequency * t);
            samples[i] = (toneA * 0.65f + toneB * 0.35f) * envelope * amplitude;
        }

        return CreateClipFromSamples(clipName, samples);
    }

    private static AudioClip CreateToneClip(
        string clipName,
        float durationSeconds,
        float frequency,
        float amplitude,
        float waveSharpness,
        float vibratoAmount,
        float vibratoFrequency)
    {
        var sampleCount = Mathf.CeilToInt(SampleRate * durationSeconds);
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)SampleRate;
            var envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(i / (float)(sampleCount - 1)));
            var modulatedFrequency = frequency * (1f + vibratoAmount * Mathf.Sin(2f * Mathf.PI * vibratoFrequency * t));
            var sine = Mathf.Sin(2f * Mathf.PI * modulatedFrequency * t);
            var brightHarmonic = Mathf.Sin(2f * Mathf.PI * modulatedFrequency * 2f * t);
            samples[i] = (sine + brightHarmonic * waveSharpness) * envelope * amplitude;
        }

        return CreateClipFromSamples(clipName, samples);
    }

    private static AudioClip CreateNoiseBurstClip(string clipName, float durationSeconds, float amplitude, float lowPassLerp)
    {
        var sampleCount = Mathf.CeilToInt(SampleRate * durationSeconds);
        var samples = new float[sampleCount];
        var state = Random.Range(-1f, 1f);

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)SampleRate;
            state = Mathf.Lerp(state, Random.Range(-1f, 1f), lowPassLerp);
            var envelope = Mathf.Exp(-6f * t) * (1f - Mathf.Clamp01(t / durationSeconds));
            samples[i] = state * amplitude * envelope;
        }

        return CreateClipFromSamples(clipName, samples);
    }

    private static AudioClip CreateFinishClip()
    {
        var durationSeconds = 0.7f;
        var sampleCount = Mathf.CeilToInt(SampleRate * durationSeconds);
        var samples = new float[sampleCount];
        var frequencies = new[] { 523.25f, 659.25f, 783.99f };

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)SampleRate;
            var envelope = Mathf.Exp(-2.8f * t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(i / (float)(sampleCount - 1)));
            var sample = 0f;

            foreach (var frequency in frequencies)
            {
                sample += Mathf.Sin(2f * Mathf.PI * frequency * t);
            }

            samples[i] = sample * (0.07f * envelope);
        }

        return CreateClipFromSamples("Finish", samples);
    }

    private static AudioClip CreateClipFromSamples(string clipName, float[] samples)
    {
        var clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
