//! Dump easing samples at 1e-3 steps (same lattice as tests/easing_goldens.rs).
//!
//! Dropped into the fetched checkout as `examples/easingdump.rs` by
//! `tools/parity/fetch_reference.sh`. Writes little-endian f64s to stdout,
//! 34 easings × 1001 samples, matching `easing_goldens.bin`.

use ttfx::utils::easing::Easing;

const EASING_GOLDEN_ORDER: &[Easing] = &[
    Easing::Linear,
    Easing::InSine,
    Easing::OutSine,
    Easing::InOutSine,
    Easing::InQuad,
    Easing::OutQuad,
    Easing::InOutQuad,
    Easing::InCubic,
    Easing::OutCubic,
    Easing::InOutCubic,
    Easing::InQuart,
    Easing::OutQuart,
    Easing::InOutQuart,
    Easing::InQuint,
    Easing::OutQuint,
    Easing::InOutQuint,
    Easing::InExpo,
    Easing::OutExpo,
    Easing::InOutExpo,
    Easing::InCirc,
    Easing::OutCirc,
    Easing::InOutCirc,
    Easing::InBack,
    Easing::OutBack,
    Easing::InOutBack,
    Easing::InElastic,
    Easing::OutElastic,
    Easing::InOutElastic,
    Easing::InBounce,
    Easing::OutBounce,
    Easing::InOutBounce,
    Easing::CubicBezier(0.25, 0.1, 0.25, 1.0),
    Easing::CubicBezier(0.42, 0.0, 0.58, 1.0),
    Easing::CubicBezier(0.68, -0.55, 0.265, 1.55),
];

fn main() {
    use std::io::Write;
    let mut out = std::io::stdout();
    for easing in EASING_GOLDEN_ORDER {
        for i in 0..=1000 {
            let p = i as f64 / 1000.0;
            let v = easing.ease(p);
            out.write_all(&v.to_le_bytes()).unwrap();
        }
    }
}
