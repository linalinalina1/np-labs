"""Generate Lab 1 charts from the measured results/results.csv.

Requires: python -m pip install matplotlib
Run from the project root: python scripts/make-charts.py
"""
import csv
from collections import defaultdict
from pathlib import Path

import matplotlib.pyplot as plt

source = Path('results/results.csv')
outdir = Path('results/charts')
if not source.is_file():
    raise SystemExit('Missing results/results.csv. Run the measurements first.')

with source.open(newline='', encoding='utf-8-sig') as handle:
    rows = list(csv.DictReader(handle))
if not rows:
    raise SystemExit('results/results.csv contains no measurements.')

outdir.mkdir(parents=True, exist_ok=True)

# Remove charts made by the old version: 1b should be compared on common axes.
for old in outdir.glob('1b_-_*_*.png'):
    old.unlink()

# Tasks 1a, 1c and 2: time, memory and speed-up by worker/thread count.
for task in ('1a', '1c', '2'):
    data = [r for r in rows if r['task'] == task]
    if not data:
        continue
    groups = defaultdict(list)
    for row in data:
        groups[int(row['threads'])].append(row)
    xs = sorted(groups)
    times = [sum(float(r['seconds']) for r in groups[x]) / len(groups[x]) for x in xs]
    memory = [sum(float(r['peak_mib']) for r in groups[x]) / len(groups[x]) for x in xs]
    for suffix, values, label in (
        ('time', times, 'Execution time (seconds)'),
        ('memory', memory, 'Peak memory (MiB)'),
    ):
        fig, ax = plt.subplots(figsize=(7, 4.5))
        ax.plot(xs, values, marker='o')
        ax.set(title=f'Task {task}: {label}', xlabel='Threads / workers', ylabel=label)
        ax.set_xticks(xs)
        ax.grid(alpha=0.25)
        fig.tight_layout()
        fig.savefig(outdir / f'{task}_{suffix}.png', dpi=160)
        plt.close(fig)
    if 1 in groups:
        baseline = times[xs.index(1)]
        fig, ax = plt.subplots(figsize=(7, 4.5))
        ax.plot(xs, [baseline / t for t in times], marker='o')
        ax.axhline(1, linestyle='--', color='gray', linewidth=1)
        ax.set(title=f'Task {task}: speed-up vs 1 thread', xlabel='Threads / workers', ylabel='Speed-up (x)')
        ax.set_xticks(xs)
        ax.grid(alpha=0.25)
        fig.tight_layout()
        fig.savefig(outdir / f'{task}_speedup.png', dpi=160)
        plt.close(fig)

# Task 1b: its thread count stays fixed. Compare methods, not thread counts.
# Show all three unsafe repetitions individually: they can have different times and totals.
b = [r for r in rows if r['task'] == '1b']
if b:
    unsafe_idx = 0
    ordered = []
    for mode in ('unsafe', 'lock', 'local'):
        for row in b:
            if row['mode'] != mode:
                continue
            if mode == 'unsafe':
                unsafe_idx += 1
                label = f'Unsafe {unsafe_idx}'
            else:
                label = mode.capitalize()
            ordered.append((label, row))

    labels = [label for label, _ in ordered]
    is_valid = [r['total_valid'].strip().lower() == 'true' for _, r in ordered]
    colors = ['#2a9d8f' if valid else '#d66a57' for valid in is_valid]
    for suffix, field, ylabel in (
        ('time', 'seconds', 'Execution time (seconds)'),
        ('memory', 'peak_mib', 'Peak memory (MiB)'),
    ):
        vals = [float(r[field]) for _, r in ordered]
        fig, ax = plt.subplots(figsize=(8.5, 4.8))
        bars = ax.bar(labels, vals, color=colors)
        ax.set(title=f'Task 1b: {ylabel} by counting method', ylabel=ylabel)
        ax.grid(axis='y', alpha=0.2)
        ax.set_axisbelow(True)
        for bar, v in zip(bars, vals):
            ax.annotate(f'{v:.3f}' if suffix == 'time' else f'{v:.1f}',
                        (bar.get_x() + bar.get_width()/2, bar.get_height()),
                        ha='center', va='bottom', fontsize=9)
        ax.set_ylim(0, max(vals) * 1.15 if max(vals) else 1)
        fig.tight_layout()
        fig.savefig(outdir / f'1b_comparison_{suffix}.png', dpi=160)
        plt.close(fig)

    # A table makes it clear that the faster unsafe versions are incorrect.
    with (outdir / '1b_correctness.csv').open('w', newline='', encoding='utf-8') as f:
        writer = csv.writer(f)
        writer.writerow(('Method', 'Threads', 'Seconds', 'Peak MiB', 'Total', 'Expected', 'Correct'))
        for label, row in ordered:
            writer.writerow((label, row['threads'], row['seconds'], row['peak_mib'],
                             row['total'], row['expected'], row['total_valid']))

print(f'Charts saved to {outdir}/')
