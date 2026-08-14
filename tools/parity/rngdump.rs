//! Dump deterministic RNG sequences from ttfx's public helpers.
//!
//! ttfx is a binary-only crate (no `lib.rs`), so this example cannot
//! `use ttfx::`. It compiles `src/utils/rng.rs` via `#[path]` instead.
//! `next_u64` and `randbelow` are private and are exercised only through
//! the public helpers. `randint(0, 2)` is a non-power-of-two range so the
//! rejection loop runs.

#[allow(dead_code)] // from_entropy is unused; next_u64/randbelow stay private
#[path = "../src/utils/rng.rs"]
mod rng;

const DEFAULT_SEED: u64 = 42;
const N: usize = 10_000;

fn parse_seed() -> u64 {
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        if arg == "--seed" {
            return args
                .next()
                .expect("--seed needs a u64")
                .parse()
                .expect("invalid --seed");
        }
    }
    DEFAULT_SEED
}

fn main() {
    let seed = parse_seed();
    println!("SEED {seed}");
    println!("COUNT {N}");

    {
        let mut r = rng::Rng::seeded(seed);
        println!("SECTION random");
        for _ in 0..N {
            println!("{:016x}", r.random().to_bits());
        }
    }

    {
        let mut r = rng::Rng::seeded(seed);
        println!("SECTION randint 0 2");
        for _ in 0..N {
            println!("{}", r.randint(0, 2));
        }
    }

    {
        let mut r = rng::Rng::seeded(seed);
        println!("SECTION randrange 0 5");
        for _ in 0..N {
            println!("{}", r.randrange(0, 5));
        }
    }

    {
        let mut r = rng::Rng::seeded(seed);
        let seq = ["a", "b", "c", "d", "e"];
        println!("SECTION choice a b c d e");
        for _ in 0..N {
            println!("{}", r.choice(&seq));
        }
    }

    {
        let mut r = rng::Rng::seeded(seed);
        println!("SECTION choice_index 7");
        for _ in 0..N {
            println!("{}", r.choice_index(7));
        }
    }

    {
        let mut r = rng::Rng::seeded(seed);
        println!("SECTION uniform 1 2");
        for _ in 0..N {
            println!("{:016x}", r.uniform(1.0, 2.0).to_bits());
        }
    }

    {
        let mut r = rng::Rng::seeded(seed);
        println!("SECTION shuffle 0 1 2 3 4 5 6 7");
        for _ in 0..N {
            let mut seq = [0i32, 1, 2, 3, 4, 5, 6, 7];
            r.shuffle(&mut seq);
            print!("{}", seq[0]);
            for x in &seq[1..] {
                print!(" {x}");
            }
            println!();
        }
    }
}
