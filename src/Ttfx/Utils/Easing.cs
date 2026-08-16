using System;
using System.Collections.Generic;

namespace Ttfx.Utils;

/// <summary>
/// A named easing or a custom cubic bezier (make_easing). Copyable so Paths and
/// Scenes can carry it by value.
/// Transcribed from <c>utils/easing.rs</c>.
/// </summary>
/// <remarks>
/// Transcription rule: Python <c>x ** n</c> routes through C <c>pow()</c> even for int
/// exponents, so every <c>**</c> here is <c>Math.Pow</c>, never <c>x * x</c> or
/// <c>Math.Sqrt</c> for <c>powf(0.5)</c> — they can differ by ULPs and coordinate
/// quantization sits downstream. Circ uses Rust <c>.sqrt()</c>;
/// that one IS <c>Math.Sqrt</c>.
/// </remarks>
public readonly record struct Easing(EasingKind Kind, double X1 = 0, double Y1 = 0, double X2 = 0, double Y2 = 0)
{
    public static Easing Linear { get; } = new(EasingKind.Linear);
    public static Easing InSine { get; } = new(EasingKind.InSine);
    public static Easing OutSine { get; } = new(EasingKind.OutSine);
    public static Easing InOutSine { get; } = new(EasingKind.InOutSine);
    public static Easing InQuad { get; } = new(EasingKind.InQuad);
    public static Easing OutQuad { get; } = new(EasingKind.OutQuad);
    public static Easing InOutQuad { get; } = new(EasingKind.InOutQuad);
    public static Easing InCubic { get; } = new(EasingKind.InCubic);
    public static Easing OutCubic { get; } = new(EasingKind.OutCubic);
    public static Easing InOutCubic { get; } = new(EasingKind.InOutCubic);
    public static Easing InQuart { get; } = new(EasingKind.InQuart);
    public static Easing OutQuart { get; } = new(EasingKind.OutQuart);
    public static Easing InOutQuart { get; } = new(EasingKind.InOutQuart);
    public static Easing InQuint { get; } = new(EasingKind.InQuint);
    public static Easing OutQuint { get; } = new(EasingKind.OutQuint);
    public static Easing InOutQuint { get; } = new(EasingKind.InOutQuint);
    public static Easing InExpo { get; } = new(EasingKind.InExpo);
    public static Easing OutExpo { get; } = new(EasingKind.OutExpo);
    public static Easing InOutExpo { get; } = new(EasingKind.InOutExpo);
    public static Easing InCirc { get; } = new(EasingKind.InCirc);
    public static Easing OutCirc { get; } = new(EasingKind.OutCirc);
    public static Easing InOutCirc { get; } = new(EasingKind.InOutCirc);
    public static Easing InBack { get; } = new(EasingKind.InBack);
    public static Easing OutBack { get; } = new(EasingKind.OutBack);
    public static Easing InOutBack { get; } = new(EasingKind.InOutBack);
    public static Easing InElastic { get; } = new(EasingKind.InElastic);
    public static Easing OutElastic { get; } = new(EasingKind.OutElastic);
    public static Easing InOutElastic { get; } = new(EasingKind.InOutElastic);
    public static Easing InBounce { get; } = new(EasingKind.InBounce);
    public static Easing OutBounce { get; } = new(EasingKind.OutBounce);
    public static Easing InOutBounce { get; } = new(EasingKind.InOutBounce);

    /// <summary>easing.make_easing(x1, y1, x2, y2): CSS-style cubic bezier.</summary>
    public static Easing MakeEasing(double x1, double y1, double x2, double y2)
        => new(EasingKind.CubicBezier, x1, y1, x2, y2);

    /// <summary>easing.make_easing's CubicBezier variant.</summary>
    public static Easing CubicBezier(double x1, double y1, double x2, double y2)
        => MakeEasing(x1, y1, x2, y2);

    public double Ease(double p)
    {
        switch (Kind)
        {
            case EasingKind.Linear:
                return p;
            case EasingKind.InSine:
                return 1.0 - Math.Cos((p * Math.PI) / 2.0);
            case EasingKind.OutSine:
                return Math.Sin((p * Math.PI) / 2.0);
            case EasingKind.InOutSine:
                return -(Math.Cos(Math.PI * p) - 1.0) / 2.0;
            case EasingKind.InQuad:
                return Math.Pow(p, 2.0);
            case EasingKind.OutQuad:
                // Rust writes this as a multiply, not powf.
                return 1.0 - (1.0 - p) * (1.0 - p);
            case EasingKind.InOutQuad:
                if (p < 0.5)
                {
                    return 2.0 * Math.Pow(p, 2.0);
                }

                return 1.0 - Math.Pow(-2.0 * p + 2.0, 2.0) / 2.0;
            case EasingKind.InCubic:
                return Math.Pow(p, 3.0);
            case EasingKind.OutCubic:
                return 1.0 - Math.Pow(1.0 - p, 3.0);
            case EasingKind.InOutCubic:
                if (p < 0.5)
                {
                    return 4.0 * Math.Pow(p, 3.0);
                }

                return 1.0 - Math.Pow(-2.0 * p + 2.0, 3.0) / 2.0;
            case EasingKind.InQuart:
                return Math.Pow(p, 4.0);
            case EasingKind.OutQuart:
                return 1.0 - Math.Pow(1.0 - p, 4.0);
            case EasingKind.InOutQuart:
                if (p < 0.5)
                {
                    return 8.0 * Math.Pow(p, 4.0);
                }

                return 1.0 - Math.Pow(-2.0 * p + 2.0, 4.0) / 2.0;
            case EasingKind.InQuint:
                return Math.Pow(p, 5.0);
            case EasingKind.OutQuint:
                return 1.0 - Math.Pow(1.0 - p, 5.0);
            case EasingKind.InOutQuint:
                if (p < 0.5)
                {
                    return 16.0 * Math.Pow(p, 5.0);
                }

                return 1.0 - Math.Pow(-2.0 * p + 2.0, 5.0) / 2.0;
            case EasingKind.InExpo:
                if (p == 0.0)
                {
                    return 0.0;
                }

                return Math.Pow(2.0, 10.0 * p - 10.0);
            case EasingKind.OutExpo:
                if (p == 1.0)
                {
                    return 1.0;
                }

                return 1.0 - Math.Pow(2.0, -10.0 * p);
            case EasingKind.InOutExpo:
                if (p == 0.0)
                {
                    return 0.0;
                }

                if (p == 1.0)
                {
                    return 1.0;
                }

                if (p < 0.5)
                {
                    return Math.Pow(2.0, 20.0 * p - 10.0) / 2.0;
                }

                return (2.0 - Math.Pow(2.0, -20.0 * p + 10.0)) / 2.0;
            case EasingKind.InCirc:
                return 1.0 - Math.Sqrt(1.0 - Math.Pow(p, 2.0)); // Circ .sqrt()
            case EasingKind.OutCirc:
                return Math.Sqrt(1.0 - Math.Pow(p - 1.0, 2.0)); // Circ .sqrt()
            case EasingKind.InOutCirc:
                if (p < 0.5)
                {
                    return (1.0 - Math.Sqrt(1.0 - Math.Pow(2.0 * p, 2.0))) / 2.0; // Circ .sqrt()
                }

                return (Math.Sqrt(1.0 - Math.Pow(-2.0 * p + 2.0, 2.0)) + 1.0) / 2.0; // Circ .sqrt()
            case EasingKind.InBack:
            {
                double c1 = 1.70158;
                double c3 = c1 + 1.0;
                return c3 * Math.Pow(p, 3.0) - c1 * Math.Pow(p, 2.0);
            }
            case EasingKind.OutBack:
            {
                double c1 = 1.70158;
                double c3 = c1 + 1.0;
                return 1.0 + c3 * Math.Pow(p - 1.0, 3.0) + c1 * Math.Pow(p - 1.0, 2.0);
            }
            case EasingKind.InOutBack:
            {
                double c1 = 1.70158;
                double c2 = c1 * 1.525;
                if (p < 0.5)
                {
                    return (Math.Pow(2.0 * p, 2.0) * ((c2 + 1.0) * 2.0 * p - c2)) / 2.0;
                }

                return (Math.Pow(2.0 * p - 2.0, 2.0) * ((c2 + 1.0) * (p * 2.0 - 2.0) + c2) + 2.0) / 2.0;
            }
            case EasingKind.InElastic:
            {
                double c4 = (2.0 * Math.PI) / 3.0;
                if (p == 0.0)
                {
                    return 0.0;
                }

                if (p == 1.0)
                {
                    return 1.0;
                }

                return -(Math.Pow(2.0, 10.0 * p - 10.0)) * Math.Sin((p * 10.0 - 10.75) * c4);
            }
            case EasingKind.OutElastic:
            {
                double c4 = (2.0 * Math.PI) / 3.0;
                if (p == 0.0)
                {
                    return 0.0;
                }

                if (p == 1.0)
                {
                    return 1.0;
                }

                return Math.Pow(2.0, -10.0 * p) * Math.Sin((p * 10.0 - 0.75) * c4) + 1.0;
            }
            case EasingKind.InOutElastic:
            {
                double c5 = (2.0 * Math.PI) / 4.5;
                if (p == 0.0)
                {
                    return 0.0;
                }

                if (p == 1.0)
                {
                    return 1.0;
                }

                if (p < 0.5)
                {
                    return -(Math.Pow(2.0, 20.0 * p - 10.0) * Math.Sin((20.0 * p - 11.125) * c5)) / 2.0;
                }

                return (Math.Pow(2.0, -20.0 * p + 10.0) * Math.Sin((20.0 * p - 11.125) * c5)) / 2.0 + 1.0;
            }
            case EasingKind.InBounce:
                return 1.0 - OutBounceFn(1.0 - p);
            case EasingKind.OutBounce:
                return OutBounceFn(p);
            case EasingKind.InOutBounce:
                if (p < 0.5)
                {
                    return (1.0 - OutBounceFn(1.0 - 2.0 * p)) / 2.0;
                }

                return (1.0 + OutBounceFn(2.0 * p - 1.0)) / 2.0;
            case EasingKind.CubicBezier:
                return BezierEasing(X1, Y1, X2, Y2, p);
            default:
                throw new InvalidOperationException($"unknown easing kind {Kind}");
        }
    }

    private static double OutBounceFn(double p)
    {
        double n1 = 7.5625;
        double d1 = 2.75;
        if (p < 1.0 / d1)
        {
            return n1 * Math.Pow(p, 2.0);
        }

        if (p < 2.0 / d1)
        {
            return n1 * Math.Pow(p - 1.5 / d1, 2.0) + 0.75;
        }

        if (p < 2.5 / d1)
        {
            return n1 * Math.Pow(p - 2.25 / d1, 2.0) + 0.9375;
        }

        return n1 * Math.Pow(p - 2.625 / d1, 2.0) + 0.984375;
    }

    /// <summary>
    /// easing.make_easing's bezier_easing: Newton-Raphson on x with the exact
    /// upstream constants (20 iterations, 1e-5 convergence, 1e-6 derivative bail).
    /// Upstream lru_caches this; we just recompute (behavior-identical).
    /// </summary>
    private static double BezierEasing(double x1, double y1, double x2, double y2, double progress)
    {
        static double SampleCurveX(double t, double x1, double x2)
            => 3.0 * x1 * Math.Pow(1.0 - t, 2.0) * t + 3.0 * x2 * (1.0 - t) * Math.Pow(t, 2.0) + Math.Pow(t, 3.0);

        static double SampleCurveY(double t, double y1, double y2)
            => 3.0 * y1 * Math.Pow(1.0 - t, 2.0) * t + 3.0 * y2 * (1.0 - t) * Math.Pow(t, 2.0) + Math.Pow(t, 3.0);

        static double SampleCurveDerivativeX(double t, double x1, double x2)
            => 3.0 * Math.Pow(1.0 - t, 2.0) * x1 + 6.0 * (1.0 - t) * t * (x2 - x1) + 3.0 * Math.Pow(t, 2.0) * (1.0 - x2);

        if (progress <= 0.0)
        {
            return 0.0;
        }

        if (progress >= 1.0)
        {
            return 1.0;
        }

        double t = progress;
        for (int i = 0; i < 20; i++)
        {
            double xEst = SampleCurveX(t, x1, x2);
            double dx = xEst - progress;
            if (Math.Abs(dx) < 1e-5)
            {
                break;
            }

            double d = SampleCurveDerivativeX(t, x1, x2);
            if (Math.Abs(d) < 1e-6)
            {
                break;
            }

            t -= dx / d;
        }

        return SampleCurveY(t, y1, y2);
    }
}

public enum EasingKind : byte
{
    Linear,
    InSine,
    OutSine,
    InOutSine,
    InQuad,
    OutQuad,
    InOutQuad,
    InCubic,
    OutCubic,
    InOutCubic,
    InQuart,
    OutQuart,
    InOutQuart,
    InQuint,
    OutQuint,
    InOutQuint,
    InExpo,
    OutExpo,
    InOutExpo,
    InCirc,
    OutCirc,
    InOutCirc,
    InBack,
    OutBack,
    InOutBack,
    InElastic,
    OutElastic,
    InOutElastic,
    InBounce,
    OutBounce,
    InOutBounce,
    CubicBezier,
}

public static class EasingParse
{
    public static Easing? Parse(string s)
    {
        return s.ToLowerInvariant() switch
        {
            "linear" => Easing.Linear,
            "in_sine" => Easing.InSine,
            "out_sine" => Easing.OutSine,
            "in_out_sine" => Easing.InOutSine,
            "in_quad" => Easing.InQuad,
            "out_quad" => Easing.OutQuad,
            "in_out_quad" => Easing.InOutQuad,
            "in_cubic" => Easing.InCubic,
            "out_cubic" => Easing.OutCubic,
            "in_out_cubic" => Easing.InOutCubic,
            "in_quart" => Easing.InQuart,
            "out_quart" => Easing.OutQuart,
            "in_out_quart" => Easing.InOutQuart,
            "in_quint" => Easing.InQuint,
            "out_quint" => Easing.OutQuint,
            "in_out_quint" => Easing.InOutQuint,
            "in_expo" => Easing.InExpo,
            "out_expo" => Easing.OutExpo,
            "in_out_expo" => Easing.InOutExpo,
            "in_circ" => Easing.InCirc,
            "out_circ" => Easing.OutCirc,
            "in_out_circ" => Easing.InOutCirc,
            "in_back" => Easing.InBack,
            "out_back" => Easing.OutBack,
            "in_out_back" => Easing.InOutBack,
            "in_elastic" => Easing.InElastic,
            "out_elastic" => Easing.OutElastic,
            "in_out_elastic" => Easing.InOutElastic,
            "in_bounce" => Easing.InBounce,
            "out_bounce" => Easing.OutBounce,
            "in_out_bounce" => Easing.InOutBounce,
            _ => null,
        };
    }
}

/// <summary>easing.EasingTracker.</summary>
public sealed class EasingTracker
{
    public Easing EasingFunction { get; }
    public long TotalSteps { get; }
    private readonly bool _clamp;
    public long CurrentStep { get; private set; }
    public double ProgressRatio { get; private set; }
    public double StepDelta { get; private set; }
    public double EasedValue { get; private set; }
    private double _lastEasedValue;

    public EasingTracker(Easing easingFunction, long totalSteps, bool clamp)
    {
        EasingFunction = easingFunction;
        TotalSteps = totalSteps;
        _clamp = clamp;
        CurrentStep = 0;
        ProgressRatio = 0.0;
        StepDelta = 0.0;
        EasedValue = 0.0;
        _lastEasedValue = 0.0;
    }

    public double Step()
    {
        if (CurrentStep < TotalSteps)
        {
            CurrentStep += 1;
            ProgressRatio = CurrentStep / (double)TotalSteps;
            EasedValue = EasingFunction.Ease(ProgressRatio);
            if (_clamp)
            {
                EasedValue = PyCompat.FMax(PyCompat.FMin(EasedValue, 1.0), 0.0);
            }

            StepDelta = EasedValue - _lastEasedValue;
            _lastEasedValue = EasedValue;
        }

        return EasedValue;
    }

    public void Reset()
    {
        CurrentStep = 0;
        ProgressRatio = 0.0;
        StepDelta = 0.0;
        EasedValue = 0.0;
        _lastEasedValue = 0.0;
    }

    public bool IsComplete() => CurrentStep >= TotalSteps;
}

/// <summary>easing.SequenceEaser over owned elements (effects use it over id groups).</summary>
public sealed class SequenceEaser<T>
{
    public List<T> Sequence { get; }
    public EasingTracker EasingTracker { get; }

    public SequenceEaser(List<T> sequence, Easing easingFunction, long totalSteps)
    {
        Sequence = sequence;
        EasingTracker = new EasingTracker(easingFunction, totalSteps, true);
    }

    public SequenceStep<T> Step()
    {
        double previousEased = EasingTracker.EasedValue;
        double easedValue = EasingTracker.Step();
        int seqLen = Sequence.Count;
        if (seqLen == 0)
        {
            return new SequenceStep<T>(Array.Empty<T>(), Array.Empty<T>());
        }

        // int() truncation, faithfully — as i64 as usize (eased_value)
        nuint length = PyCompat.TruncToUsize(easedValue * seqLen);
        nuint previousLength = PyCompat.TruncToUsize(previousEased * seqLen);
        int lengthI = ToIndex(length);
        int previousI = ToIndex(previousLength);

        if (lengthI > previousI)
        {
            return new SequenceStep<T>(Sequence.GetRange(previousI, lengthI - previousI), Array.Empty<T>());
        }

        if (lengthI < previousI)
        {
            return new SequenceStep<T>(Array.Empty<T>(), Sequence.GetRange(lengthI, previousI - lengthI));
        }

        return new SequenceStep<T>(Array.Empty<T>(), Array.Empty<T>());
    }

    public bool IsComplete() => EasingTracker.IsComplete();

    public void Reset() => EasingTracker.Reset();

    private static int ToIndex(nuint n)
    {
        if (n > (nuint)int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "usize wrap exceeds List index range");
        }

        return (int)n;
    }
}

public readonly struct SequenceStep<T>
{
    public SequenceStep(IReadOnlyList<T> added, IReadOnlyList<T> removed)
    {
        Added = added;
        Removed = removed;
    }

    public IReadOnlyList<T> Added { get; }
    public IReadOnlyList<T> Removed { get; }
}
