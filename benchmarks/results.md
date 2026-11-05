# Benchmark Results

Measured 2026-11-05 with BenchmarkDotNet 0.15.8 (ShortRun: 3 warmups, 3 iterations, 1 launch).

- Runtime: .NET 10.0.14, X64 RyuJIT x86-64-v3
- SDK: 11.0.100-rc.3.26522.148 (targets `net10.0`)
- CPU: AMD Ryzen 9 5900X, 24 logical / 12 physical cores
- OS: Windows 11 (10.0.26300.9818)
- Baseline: the original implementation (Serilog call replaced with an empty catch; not shipped)
- Each `Send` benchmark performs 10,000 sends; each subscribe benchmark performs 2,000 subscribe/unsubscribe pairs.

## Send

| Method | Subscribers | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|---:|
| Baseline_Send | 0 | 171.0 us | 1.00 | 400,000 B | 1.00 |
| New_Send | 0 | 186.4 us | 1.09 | 0 B | 0.00 |
| Baseline_Send | 1 | 2,058.8 us | 1.00 | 1,840,000 B | 1.00 |
| New_Send | 1 | 310.7 us | 0.15 | 0 B | 0.00 |
| Baseline_Send | 10 | 14,443.8 us | 1.00 | 9,760,000 B | 1.00 |
| New_Send | 10 | 1,052.2 us | 0.07 | 0 B | 0.00 |

## Send with a struct payload

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|
| Baseline_SendStruct | 1,820.0 us | 1.00 | 2,080,000 B | 1.00 |
| New_SendStruct | 299.1 us | 0.16 | 0 B | 0.00 |

## Subscribe / unsubscribe

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|
| Baseline_SubscribeUnsubscribe | 1.094 ms | 1.00 | 1,171.88 KB | 1.00 |
| New_SubscribeUnsubscribe | 1.353 ms | 1.25 | 970.72 KB | 0.83 |

## Notes

- `Send` allocates **0 bytes** in every measured scenario, including struct payloads and ten subscribers.
- The no-subscriber case is within run-to-run noise; both implementations perform one lookup and return.
- Subscribe/unsubscribe does slightly more work per write (copy-on-write array plus the weak anchor), but
  allocates about 17% less than the baseline.
- Reproduce with `dotnet run --project benchmarks/AnoiMessagingCenter.Benchmarks -c Release -- --filter "*" --job short`.
