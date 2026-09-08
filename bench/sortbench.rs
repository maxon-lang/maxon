// Rust baseline for the Maxon sort benchmark. Mirrors bench/sortbench.maxon
// exactly: same LCG input, same patterns (random/ascending/descending), same
// element types (i64 and String), clone-then-sort per timed iteration, same
// CSV output. Uses slice::sort (the stable sort = driftsort, the algorithm
// Maxon's Array.sort reproduces), so this is a same-algorithm, same-machine
// comparison. Build with `rustc -O` (release) — driftsort is only fast there.
//
//   lang,type,pattern,n,iters,total_ms

use std::time::Instant;

// Matches the Maxon LCG: (state * 1103515245 + 12345) & 0x7FFFFFFF, wrapping.
fn lcg(state: i64) -> i64 {
    (state.wrapping_mul(1103515245).wrapping_add(12345)) & 0x7FFF_FFFF
}

fn build_i64(n: i64, pattern: i32) -> Vec<i64> {
    let mut a = Vec::with_capacity(n as usize);
    let mut r: i64 = 2463534242;
    for i in 0..n {
        match pattern {
            1 => a.push(i),
            2 => a.push(n - i),
            _ => {
                r = lcg(r);
                a.push(r & 0xFFFF);
            }
        }
    }
    a
}

fn build_str(n: i64, pattern: i32) -> Vec<String> {
    let mut a = Vec::with_capacity(n as usize);
    let mut r: i64 = 2463534242;
    for i in 0..n {
        let v = match pattern {
            1 => i,
            2 => n - i,
            _ => {
                r = lcg(r);
                r & 0xFFFF
            }
        };
        a.push(format!("{}", v));
    }
    a
}

fn bench_i64(pattern: i32, pat_name: &str, n: i64, iters: i64) {
    let master = build_i64(n, pattern);
    // Clone-only baseline, subtracted so we report sort time alone.
    let clone_start = Instant::now();
    for _ in 0..iters {
        let c = master.clone();
        std::hint::black_box(&c);
    }
    let clone_ms = clone_start.elapsed().as_millis();
    let start = Instant::now();
    for _ in 0..iters {
        let mut work = master.clone();
        work.sort();
        std::hint::black_box(&work);
    }
    let ms = start.elapsed().as_millis();
    let sort_ms = ms.saturating_sub(clone_ms);
    println!("rust,i64,{},{},{},{}", pat_name, n, iters, sort_ms);
}

fn bench_str(pattern: i32, pat_name: &str, n: i64, iters: i64) {
    let master = build_str(n, pattern);
    let clone_start = Instant::now();
    for _ in 0..iters {
        let c = master.clone();
        std::hint::black_box(&c);
    }
    let clone_ms = clone_start.elapsed().as_millis();
    let start = Instant::now();
    for _ in 0..iters {
        let mut work = master.clone();
        work.sort();
        std::hint::black_box(&work);
    }
    let ms = start.elapsed().as_millis();
    let sort_ms = ms.saturating_sub(clone_ms);
    println!("rust,str,{},{},{},{}", pat_name, n, iters, sort_ms);
}

fn main() {
    let n: i64 = 20000;
    let iters: i64 = 100;
    println!("lang,type,pattern,n,iters,total_ms");
    bench_i64(0, "random", n, iters);
    bench_i64(1, "ascending", n, iters);
    bench_i64(2, "descending", n, iters);
    bench_str(0, "random", n, iters);
    bench_str(1, "ascending", n, iters);
    bench_str(2, "descending", n, iters);
}
