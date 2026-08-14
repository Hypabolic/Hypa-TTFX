namespace Ttfx.Utils;

/// <summary>
/// Named easings only. Evaluation is issue 0009.
/// </summary>
public enum Easing
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
