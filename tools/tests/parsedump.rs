//! Probe Rust `str::parse` for i64 / u64 / f64.
//!
//! Compiled with `rustc` by `gen_numeric_corpus.py` (not via cargo).
//! Usage: parsedump <i64|u64|f64> <token>

fn main() {
    let mut args = std::env::args().skip(1);
    let kind = args.next().expect("usage: parsedump <i64|u64|f64> <token>");
    let token = args.next().expect("usage: parsedump <i64|u64|f64> <token>");
    match kind.as_str() {
        "i64" => match token.parse::<i64>() {
            Ok(v) => println!("accept {v}"),
            Err(_) => println!("reject"),
        },
        "u64" => match token.parse::<u64>() {
            Ok(v) => println!("accept {v}"),
            Err(_) => println!("reject"),
        },
        "f64" => match token.parse::<f64>() {
            Ok(v) => println!("accept {:016x}", v.to_bits()),
            Err(_) => println!("reject"),
        },
        other => {
            eprintln!("unknown kind: {other}");
            std::process::exit(2);
        }
    }
}
