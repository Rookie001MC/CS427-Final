using System;
using UnityEngine;

/// <summary>
/// Accumulates elapsed run time. Deliberately dumb: it does not know about countdowns,
/// deaths or the finish line - <see cref="GameManager"/> drives it.
/// </summary>
public sealed class RunTimer : MonoBehaviour
{
    /// <summary>Seconds elapsed in the current run.</summary>
    public float ElapsedSeconds { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>Raised every frame the timer advances. Payload is <see cref="ElapsedSeconds"/>.</summary>
    public event Action<float> Ticked;

    private void Update()
    {
        if (!IsRunning)
        {
            return;
        }

        // Time.deltaTime is already 0 while Time.timeScale is 0, so a timeScale pause and an
        // explicit Pause() both stop the clock. Belt and braces is intentional here.
        ElapsedSeconds += Time.deltaTime;
        Ticked?.Invoke(ElapsedSeconds);
    }

    /// <summary>Zeroes the clock and leaves it stopped.</summary>
    public void ResetTimer()
    {
        ElapsedSeconds = 0f;
        IsRunning = false;
        Ticked?.Invoke(0f);
    }

    /// <summary>Zeroes the clock and starts it. Used when the countdown reaches GO.</summary>
    public void Begin()
    {
        ElapsedSeconds = 0f;
        IsRunning = true;
    }

    public void Pause() => IsRunning = false;

    public void Resume() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>Formats seconds as MM:SS.hh, e.g. 84.38 -> "01:24.38".</summary>
    public static string Format(float seconds)
    {
        if (seconds < 0f || float.IsNaN(seconds))
        {
            seconds = 0f;
        }

        int minutes = (int)(seconds / 60f);
        float remainder = seconds - minutes * 60f;
        int wholeSeconds = (int)remainder;
        int hundredths = (int)((remainder - wholeSeconds) * 100f);

        return $"{minutes:00}:{wholeSeconds:00}.{hundredths:00}";
    }

    /// <summary>Formats a signed difference, e.g. "-00:02.14".</summary>
    public static string FormatDelta(float deltaSeconds)
    {
        string sign = deltaSeconds >= 0f ? "+" : "-";
        return sign + Format(Mathf.Abs(deltaSeconds));
    }
}
