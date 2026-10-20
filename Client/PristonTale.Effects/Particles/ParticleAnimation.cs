namespace PristonTale.Effects.Particles;

public readonly record struct ParticleAnimationFrame(int Image, float Duration, float Alpha);
public readonly record struct ParticleAnimationSample(int Image, float Alpha);

public sealed class ParticleAnimation
{
    private readonly ParticleAnimationFrame[] _frames;
    private readonly float _startAlpha;
    private readonly float _loopDuration;

    public ParticleAnimation(IEnumerable<ParticleAnimationFrame> frames, float startAlpha)
    {
        _frames = frames.ToArray();
        if (_frames.Length < 2 || !float.IsFinite(startAlpha) || startAlpha is < 0f or > 1f)
        {
            throw new ArgumentException("Animation needs at least two frames and a valid starting alpha.");
        }

        _startAlpha = startAlpha;
        for (var index = 0; index < _frames.Length; index++)
        {
            var frame = _frames[index];
            if (frame.Image < 0 || !float.IsFinite(frame.Duration) || frame.Duration <= 0f
                || !float.IsFinite(frame.Alpha) || frame.Alpha is < 0f or > 1f)
            {
                throw new ArgumentException("Invalid particle animation frame.");
            }

            Duration += frame.Duration;
            if (index > 0 && index < _frames.Length - 1)
            {
                _loopDuration += frame.Duration;
            }
        }
    }

    public float Duration { get; }
    public float ExitDuration => _frames[^1].Duration;

    public ParticleAnimationSample Sample(float age, float exitAt = float.PositiveInfinity)
    {
        age = MathF.Max(0f, age);
        if (float.IsFinite(exitAt) && age >= exitAt)
        {
            // The original billboard update resets alpha to the penultimate frame when physics expires.
            return Interpolate(_frames[^1], _frames[^2].Alpha, age - exitAt);
        }

        var alpha = _startAlpha;
        var first = 0;
        var last = _frames.Length;
        if (float.IsFinite(exitAt) && _loopDuration > 0f && age >= _frames[0].Duration)
        {
            age -= _frames[0].Duration;
            alpha = age >= _loopDuration ? _frames[^2].Alpha : _frames[0].Alpha;
            age %= _loopDuration;
            first = 1;
            last--;
        }

        for (var index = first; index < last; index++)
        {
            var frame = _frames[index];
            if (age < frame.Duration)
            {
                return Interpolate(frame, alpha, age);
            }

            age -= frame.Duration;
            alpha = frame.Alpha;
        }

        return new ParticleAnimationSample(_frames[^1].Image, _frames[^1].Alpha);
    }

    private static ParticleAnimationSample Interpolate(ParticleAnimationFrame frame, float start, float age)
    {
        var fraction = Math.Clamp(age / frame.Duration, 0f, 1f);
        return new ParticleAnimationSample(frame.Image, start + (frame.Alpha - start) * fraction);
    }
}
