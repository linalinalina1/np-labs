# Laboratory Work 1: Concurrency in Practice

## 1. Goal
Measure the effect of thread count and work partitioning on execution time and peak memory; distinguish CPU-bound from I/O-bound workloads; study shared-counter races and synchronization; identify where parallel speed-up stops.

## 2. Source code
Git repository: **[add the link after pushing the project]**.

## 3. Hardware and software

| Item | Actual configuration |
| --- | --- |
| CPU model | [fill in] |
| Physical CPU cores | [fill in] |
| Logical processors | [fill in] |
| RAM | [fill in] |
| Internal disk type | [fill in] |
| USB drive (model / capacity) | [fill in] |
| USB version / port | [fill in] |
| Operating system | [fill in] |
| Programming language | C# |
| Runtime and SDK | [.NET version actually installed] |

## 4. Input data and validation
`numbers.txt`: 50,000,000 random integers in [-1,000,000; 1,000,000], one per line. Eight ordered partitions of 6,250,000 integers are used for Task 2. State the measured negative, positive and zero totals from a correct run. Check that all correct tasks produce **identical** sign counts and that their sum is 50,000,000. The unsynchronized Task 1b mode is deliberately allowed to be incorrect.

## 5. Task 1a: Counting one file with threads

Insert the measurements for 1, 2, 4, 8, 16, 32 and 64 threads. Include charts of execution time, speed-up (T1 / Tn), and peak working set versus threads.

**Question 1.** At what number of threads does speed-up stop increasing? Compare the result to the physical core and logical processor counts. **Answer using the actual chart:** [fill in].

**Question 2.** Why might 32 and 64 threads fail to improve performance? **Answer:** Extra scheduling, context switches, memory bandwidth contention, and I/O competition can outweigh gains after available CPU parallelism is saturated. Identify whether they were slower in the actual results: [fill in].

**Question 3.** Does memory change with thread count? **Answer:** Each additional OS thread requires its own stack and per-thread buffers. However, caching and runtime effects can mask a clean linear pattern. Discuss the measured results: [fill in].

## 6. Task 1b: Three synchronization strategies

The input is loaded into an `int[]` *before* timing. The thread count equals the runtime-reported logical processor count. Record the three independent unsynchronized runs and one locked and one thread-local run.

| Mode | Time (s) | Negative | Positive | Zero | Correct? |
| --- | --- | --- | --- | --- | --- |
| unsafe, run 1 | [ ] | [ ] | [ ] | [ ] | [ ] |
| unsafe, run 2 | [ ] | [ ] | [ ] | [ ] | [ ] |
| unsafe, run 3 | [ ] | [ ] | [ ] | [ ] | [ ] |
| lock | [ ] | [ ] | [ ] | [ ] | [ ] |
| local | [ ] | [ ] | [ ] | [ ] | [ ] |

Include time and peak-memory comparisons for the three versions; a speed-up versus thread count is not meaningful because thread count is fixed for 1b.

**Question 4.** Rank by observed time: [fill in]. **Correctness:** The locked and thread-local strategies avoid lost updates; unsynchronized shared increments can lose updates. A lock serializes each counter increment, while local counters avoid contention and combine only after the worker threads join.

## 7. Task 1c: Producer and consumers

The producer reads lines in batches of 10,000. The bounded queue holds at most 100 batches. Consumers wait when the queue is empty; the producer waits when full. Insert time, speed-up and peak-memory charts for 1, 2, 4, 8, 16, 32 and 64 workers.

**Question 5.** Compare Task 1c and Task 1a at each matching worker/thread count. Identify the faster implementation and the saturation point from the actual timings: [fill in]. The producer is a single-threaded reading stage, so it may limit throughput; the queue adds allocation and synchronization costs.

**Question 6.** With batches of one line, each queue operation would carry very little work and coordination overhead would grow substantially. An unbounded queue could grow faster than the consumers drain it, potentially causing high memory use or resource exhaustion.

## 8. Task 2: Eight files on USB

Before each run, safely eject, physically disconnect and reconnect the USB drive. Read the files only from the USB path. Use 1, 2 and 4 threads. Insert execution time, speed-up and peak-memory charts.

**Question 1.** Compare the one-thread USB timing with the one-thread internal-disk timing for Task 1a. Give both numbers and the ratio: [fill in]. Decide whether USB I/O is the bottleneck based on measurements: [fill in].

**Question 2.** Do multiple threads help? [fill in measured times]. One physical flash drive has finite I/O bandwidth and may not gain throughput from competing bulk reads. This differs from many independent waits, such as concurrent network requests, where waiting times can overlap.

## 9. Control questions

1. **Physical vs logical processor:** A physical core is a hardware execution core; a logical processor is a scheduling context visible to the OS (for example, two logical contexts on one physical core under SMT). Relate the distinction to the measured saturation: [fill in].
2. **Range-boundary correctness:** Divide by byte positions; when a range begins inside a line, skip to the next newline; process a line only when its first byte belongs to the thread's partition. Thus a full line belongs to exactly one thread.
3. **Lost updates:** Two threads read the same counter value, each adds one, and both write back that same next value. Two increments occur but only one increment is reflected in the stored result.
4. **Per-thread versus lock:** Local counters eliminate the repeated synchronization and cache-line contention of shared counters. Combining the local results after `Join` needs very little coordination.
5. **USB vs server network I/O:** I/O-bound means progress is limited primarily by waiting on input/output rather than CPU computation. A single USB device can have a shared transfer bottleneck, while network requests to independent endpoints can overlap waits and improve throughput/concurrency.
6. **Static work division vs queue:** Static partitioning minimizes coordination when work is balanced and predictable. A queue dynamically assigns work when task lengths vary, at the cost of synchronization and queue management.

## 10. Conclusion

Replace with a conclusion supported by your charts: identify which tasks were limited by CPU processing, disk transfer, queue/producer throughput, or synchronization, and state the measured point where adding threads stopped helping. Do not assert CPU-bound or I/O-bound without the measurements.
