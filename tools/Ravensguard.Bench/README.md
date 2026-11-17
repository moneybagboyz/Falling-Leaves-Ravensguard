# Ravensguard.Bench

Stopwatch micro-benchmarks for sim hot paths (melee skirmish step, `LightGrid.Compute`). Run: `dotnet run --project tools/Ravensguard.Bench -c Release -- [--iterations 20]`. Prints median and p95; allocation gates and BenchmarkDotNet come later.
