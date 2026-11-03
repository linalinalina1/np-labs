# Laboratory Work 1: Concurrency in Practice

One C# / .NET 8 command-line application implementing Tasks 1a, 1b, 1c, and 2 from the laboratory sheet.

## Prerequisites

- .NET 8 SDK (`dotnet --version`)
- Enough free disk space for `numbers.txt`, eight split files and USB copies (several hundred MB per set)
- A USB flash drive for Task 2
- Windows PowerShell for automated local measurements (or run the same commands individually)

## Generate the required data

```powershell
dotnet run -c Release -- generate --file numbers.txt --count 50000000 --seed 12345
dotnet run -c Release -- split --file numbers.txt --out parts --count 50000000
```

`numbers.txt` contains **50,000,000** random integers from **-1,000,000 to 1,000,000**, one per line. `parts/part1.txt` through `part8.txt` contain 6,250,000 consecutive lines each, in original order. Use the same generated data for every task.

## Run the tasks

```powershell
dotnet build -c Release
dotnet run -c Release -- 1a --file numbers.txt --threads 4
dotnet run -c Release -- 1b --file numbers.txt --mode unsafe
dotnet run -c Release -- 1b --file numbers.txt --mode lock
dotnet run -c Release -- 1b --file numbers.txt --mode local
dotnet run -c Release -- 1c --file numbers.txt --threads 4
```

For complete Task 1a and 1c results use thread counts `1, 2, 4, 8, 16, 32, 64`. Task 1b uses `Environment.ProcessorCount` logical processors, loads the array before the timer, and must run unsafe mode three times.

On Windows, automate local runs with:

```powershell
.\scripts\run-local.ps1
```

### Task 2 (USB)

1. Copy the entire `parts` directory to your USB drive, e.g. `E:\parts`.
2. Before **each** test, safely eject the USB, physically unplug it and reconnect it. The program cannot automate this correctly and the script prompts you.
3. Run 1, 2 and 4 threads, one test each:

```powershell
.\scripts\run-usb.ps1 -UsbPartsDirectory 'E:\parts'
```

Task 2 uses `File.ReadAllBytes` once per file. Each worker receives whole files, never partial files.

## Measurement output and correctness

Each invocation writes one row to `results/results.csv` with time, peak working set, sign totals and validity. Run **each configuration as a new OS process**; peak working set is a process high-water mark and otherwise persists across experiments. Timings include thread startup, reading, parsing and joining; Task 1b intentionally excludes the initial array load. The unsafe version is expected to sometimes lose increments, so it is recorded even if incorrect. The other modes must total 50,000,000; additionally, the three sign counts must match across correct modes.

Create time, memory, and (where a one-thread baseline exists) speed-up charts:

```powershell
python -m pip install matplotlib
python scripts/make-charts.py
```

Task 1b has one fixed logical-processor count for all modes, so a thread-count speed-up curve is not applicable. Compare its three modes by time and correctness in the report instead.

## Smaller smoke test (not report data)

Use e.g. `--count 80000` in **generate, split and all task invocations** to validate functionality before producing the full 50-million-line file. This is not a substitute for the required official experiment.

## Implementation overview

- **1a**: partition by byte offsets and align each partition to the next complete line; each OS thread reads only its own range and uses private counters.
- **1b**: unsafe shared increments; locked increments; private per-thread counters reduced after `Join`.
- **1c**: one dedicated producer, a `BlockingCollection<string[]>` bounded to 100 batches, and dedicated worker threads; batches contain 10,000 lines (last batch may be shorter).
- **2**: exactly eight input files read from USB, evenly distributed by whole file and loaded in a single bulk operation per file.

## Report

See `REPORT_TEMPLATE.md`. Replace hardware fields, repository URL and result placeholders with actual measurements. Do not report example or smoke-test numbers as measurements.
