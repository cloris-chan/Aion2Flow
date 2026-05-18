using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class ScenePlaybackController(SceneRuntimePlayback playback)
{
    private double _speed = 1d;

    public SceneRuntimePlayback Playback { get; } = playback;
    public bool IsPlaying { get; private set; }
    public long PositionMilliseconds { get; private set; }
    public long DurationMilliseconds => Math.Max(0, Playback.EndTimeMilliseconds - Playback.StartTimeMilliseconds);

    public double Speed
    {
        get => _speed;
        set => _speed = value > 0 && double.IsFinite(value) ? value : 1d;
    }

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    public void Toggle()
    {
        IsPlaying = !IsPlaying;
    }

    public void Seek(long positionMilliseconds)
    {
        PositionMilliseconds = Math.Clamp(positionMilliseconds, 0, DurationMilliseconds);
    }

    public void SeekRatio(double ratio)
    {
        if (!double.IsFinite(ratio))
            ratio = 0;

        Seek((long)Math.Round(DurationMilliseconds * Math.Clamp(ratio, 0d, 1d)));
    }

    public void Advance(long elapsedMilliseconds)
    {
        if (!IsPlaying || elapsedMilliseconds <= 0)
            return;

        var delta = (long)Math.Round(elapsedMilliseconds * Speed);
        Seek(PositionMilliseconds + delta);
        if (PositionMilliseconds >= DurationMilliseconds)
            IsPlaying = false;
    }

    public SceneCombatSnapshot CreateSnapshot()
        => Playback.CreateSnapshotAt(ToObservedAtMilliseconds(PositionMilliseconds));

    public SceneReadModelFrame CreateFrame(int detailCombatantId = 0, bool forceDetailRefresh = false)
        => Playback.CreateFrameAt(ToObservedAtMilliseconds(PositionMilliseconds), detailCombatantId, forceDetailRefresh);

    private long ToObservedAtMilliseconds(long positionMilliseconds)
        => Playback.StartTimeMilliseconds + Math.Clamp(positionMilliseconds, 0, DurationMilliseconds);
}
