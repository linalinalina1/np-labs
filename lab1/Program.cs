using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

internal static class Program
{
    private const long DefaultCount = 50_000_000;
    private const int BatchSize = 10_000;
    private const int QueueCapacity = 100;
    private static readonly int[] ThreadOptions = { 1, 2, 4, 8, 16, 32, 64 };

    private struct Counts
    {
        public long Negative, Positive, Zero;
        public readonly long Total => Negative + Positive + Zero;
        public void Add(int number)
        {
            if (number < 0) Negative++;
            else if (number > 0) Positive++;
            else Zero++;
        }
        public void Merge(Counts other)
        {
            Negative += other.Negative;
            Positive += other.Positive;
            Zero += other.Zero;
        }
    }

    private sealed class SharedCounts
    {
        public long Negative, Positive, Zero;
        public Counts Snapshot() => new() { Negative = Negative, Positive = Positive, Zero = Zero };
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help") { Help(); return 0; }
            var options = ParseOptions(args.Skip(1).ToArray());
            string data = Get(options, "file", "numbers.txt");
            long expected = LongOption(options, "count", DefaultCount);
            if (expected <= 0) throw new ArgumentException("--count must be positive.");
            switch (args[0].ToLowerInvariant())
            {
                case "generate": Generate(data, expected, IntOption(options, "seed", 12345)); return 0;
                case "split": Split(data, Get(options, "out", "parts"), expected); return 0;
                case "1a":
                case "1c":
                {
                    int threads = IntOption(options, "threads", 1);
                    if (!ThreadOptions.Contains(threads)) throw new ArgumentException("--threads must be 1,2,4,8,16,32,64.");
                    Execute(args[0], threads, "-", expected, () => args[0] == "1a" ? CountByRanges(data, threads) : ProducerConsumers(data, threads), options);
                    return 0;
                }
                case "1b":
                {
                    string mode = Get(options, "mode", "local").ToLowerInvariant();
                    if (mode is not ("unsafe" or "lock" or "local")) throw new ArgumentException("--mode: unsafe, lock, local.");
                    int threads = Environment.ProcessorCount;
                    Console.WriteLine($"Loading {expected:N0} integers before measurement...");
                    int[] values = LoadArray(data, expected);
                    Execute("1b", threads, mode, expected, () => CountArray(values, threads, mode), options);
                    return 0;
                }
                case "2":
                {
                    int threads = IntOption(options, "threads", 1);
                    if (threads is not (1 or 2 or 4)) throw new ArgumentException("Task 2 threads: 1, 2, 4.");
                    string directory = Get(options, "dir", "parts");
                    string[] files = Enumerable.Range(1, 8).Select(i => Path.Combine(directory, $"part{i}.txt")).ToArray();
                    foreach (string path in files) if (!File.Exists(path)) throw new FileNotFoundException("Missing input file on USB drive", path);
                    Execute("2", threads, "-", expected, () => CountFiles(files, threads), options);
                    return 0;
                }
                default: throw new ArgumentException("Unknown task. Run: dotnet run -- help");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"ERROR: {ex.Message}"); return 1; }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new ArgumentException("Use --option value pairs.");
            if (!result.TryAdd(args[i][2..], args[i + 1])) throw new ArgumentException($"Repeated option: {args[i]}");
        }
        return result;
    }
    private static string Get(Dictionary<string, string> options, string name, string fallback) => options.GetValueOrDefault(name, fallback);
    private static int IntOption(Dictionary<string, string> options, string name, int fallback) => options.TryGetValue(name, out var text) ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;
    private static long LongOption(Dictionary<string, string> options, string name, long fallback) => options.TryGetValue(name, out var text) ? long.Parse(text, CultureInfo.InvariantCulture) : fallback;

    private static void Generate(string path, long count, int seed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var random = new Random(seed);
        using var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20), new UTF8Encoding(false), 1 << 20);
        for (long i = 0; i < count; i++) writer.WriteLine(random.Next(-1_000_000, 1_000_001));
        Console.WriteLine($"Generated {count:N0} integers in {path}, seed {seed}.");
    }

    private static void Split(string source, string directory, long count)
    {
        if (count % 8 != 0) throw new ArgumentException("--count must be divisible by 8 for split.");
        Directory.CreateDirectory(directory);
        using var reader = new StreamReader(source);
        for (int i = 1; i <= 8; i++)
        {
            using var writer = new StreamWriter(Path.Combine(directory, $"part{i}.txt"), false, new UTF8Encoding(false), 1 << 20);
            for (long j = 0; j < count / 8; j++) writer.WriteLine(reader.ReadLine() ?? throw new InvalidDataException("Input has fewer lines than expected."));
        }
        if (reader.ReadLine() != null) throw new InvalidDataException("Input has more lines than expected.");
        Console.WriteLine($"Created 8 ordered files in {directory} ({count / 8:N0} lines each).");
    }

    // A byte position defines the start of each partition. A thread starts at the first
    // complete line at/after its start boundary and owns lines whose first byte < end.
    private static Counts CountByteRange(string path, long start, long end)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        long length = stream.Length;
        if (start > 0)
        {
            stream.Position = start - 1;
            int previous = stream.ReadByte();
            if (previous != '\n')
            {
                int character;
                while ((character = stream.ReadByte()) != -1 && character != '\n') { }
            }
        }
        else stream.Position = 0;

        Counts counts = default;
        byte[] buffer = new byte[1 << 16];
        long position = stream.Position;
        long lineStart = position;
        long accumulator = 0;
        bool negative = false, hasDigits = false;
        while (lineStart < end)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                if (b == '\n')
                {
                    if (!hasDigits) throw new InvalidDataException("Empty or invalid line.");
                    counts.Add(checked((int)(negative ? -accumulator : accumulator)));
                    lineStart = position + i + 1;
                    accumulator = 0; negative = false; hasDigits = false;
                    if (lineStart >= end) return counts;
                }
                else if (b == '-' && !hasDigits && accumulator == 0) negative = true;
                else if (b is >= (byte)'0' and <= (byte)'9') { accumulator = checked(accumulator * 10 + b - '0'); hasDigits = true; }
                else if (b != '\r') throw new InvalidDataException($"Unexpected character at byte {position + i}.");
            }
            position += read;
        }
        if (hasDigits && lineStart < end) counts.Add(checked((int)(negative ? -accumulator : accumulator)));
        return counts;
    }

    private static Counts CountByRanges(string path, int threadCount)
    {
        long bytes = new FileInfo(path).Length;
        var partial = new Counts[threadCount];
        RunThreads(threadCount, i =>
        {
            long start = bytes * i / threadCount;
            long end = bytes * (i + 1) / threadCount;
            partial[i] = CountByteRange(path, start, end);
        });
        return Combine(partial);
    }

    private static int[] LoadArray(string path, long expected)
    {
        if (expected > int.MaxValue) throw new ArgumentException("Array size exceeds int limit.");
        var result = new int[(int)expected];
        using var reader = new StreamReader(path);
        for (int i = 0; i < result.Length; i++) result[i] = int.Parse(reader.ReadLine() ?? throw new InvalidDataException("Input ended early."), CultureInfo.InvariantCulture);
        if (reader.ReadLine() != null) throw new InvalidDataException("Input contains extra lines.");
        return result;
    }

    private static Counts CountArray(int[] data, int threadCount, string mode)
    {
        var shared = new SharedCounts();
        var local = new Counts[threadCount];
        object gate = new();
        RunThreads(threadCount, i =>
        {
            int from = (int)((long)data.Length * i / threadCount);
            int until = (int)((long)data.Length * (i + 1) / threadCount);
            Counts own = default;
            for (int j = from; j < until; j++)
            {
                int value = data[j];
                if (mode == "local") own.Add(value);
                else if (mode == "lock")
                {
                    lock (gate)
                    {
                        if (value < 0) shared.Negative++;
                        else if (value > 0) shared.Positive++;
                        else shared.Zero++;
                    }
                }
                else
                {
                    // Demonstration only: these shared read-modify-write operations race.
                    if (value < 0) shared.Negative++;
                    else if (value > 0) shared.Positive++;
                    else shared.Zero++;
                }
            }
            local[i] = own;
        });
        return mode == "local" ? Combine(local) : shared.Snapshot();
    }

    private static Counts ProducerConsumers(string path, int workerCount)
    {
        using var queue = new BlockingCollection<string[]>(QueueCapacity);
        var workers = new Thread[workerCount];
        var partial = new Counts[workerCount];
        var errors = new ConcurrentQueue<Exception>();
        for (int i = 0; i < workerCount; i++)
        {
            int slot = i;
            workers[i] = new Thread(() =>
            {
                try
                {
                    Counts own = default;
                    foreach (string[] batch in queue.GetConsumingEnumerable())
                        foreach (string line in batch) own.Add(int.Parse(line, CultureInfo.InvariantCulture));
                    partial[slot] = own;
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            });
            workers[i].Start();
        }
        var producer = new Thread(() =>
        {
            try
            {
                using var reader = new StreamReader(path);
                while (true)
                {
                    var lines = new List<string>(BatchSize);
                    for (int i = 0; i < BatchSize; i++)
                    {
                        string? line = reader.ReadLine();
                        if (line is null) break;
                        lines.Add(line);
                    }
                    if (lines.Count == 0) break;
                    queue.Add(lines.ToArray());
                }
            }
            catch (Exception ex) { errors.Enqueue(ex); }
            finally { queue.CompleteAdding(); }
        });
        producer.Start(); producer.Join();
        foreach (Thread worker in workers) worker.Join();
        if (!errors.IsEmpty) throw new AggregateException(errors);
        return Combine(partial);
    }

    private static Counts CountFiles(string[] files, int threadCount)
    {
        var partial = new Counts[threadCount];
        RunThreads(threadCount, i =>
        {
            Counts own = default;
            for (int file = i; file < files.Length; file += threadCount)
            {
                byte[] bytes = File.ReadAllBytes(files[file]); // Exactly one bulk read per file.
                own.Merge(ParseBytes(bytes));
            }
            partial[i] = own;
        });
        return Combine(partial);
    }

    private static Counts ParseBytes(byte[] bytes)
    {
        Counts result = default;
        long value = 0;
        bool negative = false, digits = false;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b == '\n')
            {
                if (!digits) throw new InvalidDataException("Invalid line in USB file.");
                result.Add(checked((int)(negative ? -value : value)));
                value = 0; negative = false; digits = false;
            }
            else if (b == '-' && !digits && value == 0) negative = true;
            else if (b is >= (byte)'0' and <= (byte)'9') { value = checked(value * 10 + b - '0'); digits = true; }
            else if (b != '\r') throw new InvalidDataException("Invalid byte in USB file.");
        }
        if (digits) result.Add(checked((int)(negative ? -value : value)));
        return result;
    }

    private static void RunThreads(int count, Action<int> action)
    {
        var failures = new ConcurrentQueue<Exception>();
        Thread[] threads = Enumerable.Range(0, count).Select(i => new Thread(() =>
        {
            try { action(i); }
            catch (Exception ex) { failures.Enqueue(ex); }
        })).ToArray();
        foreach (Thread thread in threads) thread.Start();
        foreach (Thread thread in threads) thread.Join();
        if (!failures.IsEmpty) throw new AggregateException(failures);
    }

    private static Counts Combine(IEnumerable<Counts> parts)
    {
        Counts result = default;
        foreach (Counts part in parts) result.Merge(part);
        return result;
    }

    private static void Execute(string task, int threads, string mode, long expected, Func<Counts> work, Dictionary<string, string> options)
    {
        using var process = Process.GetCurrentProcess();
        var watch = Stopwatch.StartNew();
        Counts result = work();
        watch.Stop();
        process.Refresh();
        long peakBytes = process.PeakWorkingSet64;
        bool valid = result.Total == expected;
        Console.WriteLine($"Task={task} Mode={mode} Threads={threads} Time={watch.Elapsed.TotalSeconds:F3}s PeakWorkingSet={peakBytes / 1048576.0:F2} MiB");
        Console.WriteLine($"Negative={result.Negative} Positive={result.Positive} Zero={result.Zero} Total={result.Total} Expected={expected} TotalValid={valid}");
        string output = Get(options, "csv", "results/results.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        bool header = !File.Exists(output) || new FileInfo(output).Length == 0;
        using var writer = new StreamWriter(output, append: true);
        if (header) writer.WriteLine("utc,task,mode,threads,seconds,peak_mib,negative,positive,zero,total,expected,total_valid");
        writer.WriteLine(string.Join(",", DateTime.UtcNow.ToString("O"), task, mode, threads.ToString(CultureInfo.InvariantCulture),
            watch.Elapsed.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture),
            (peakBytes / 1048576.0).ToString("F3", CultureInfo.InvariantCulture),
            result.Negative.ToString(CultureInfo.InvariantCulture), result.Positive.ToString(CultureInfo.InvariantCulture), result.Zero.ToString(CultureInfo.InvariantCulture), result.Total.ToString(CultureInfo.InvariantCulture), expected.ToString(CultureInfo.InvariantCulture), valid.ToString().ToLowerInvariant()));
        if (!valid && mode != "unsafe") throw new InvalidDataException($"Count mismatch: {result.Total} != {expected}.");
    }

    private static void Help() => Console.WriteLine("""
        Concurrency Lab 1 (.NET 8)
        dotnet run -c Release -- generate --file numbers.txt --count 50000000 --seed 12345
        dotnet run -c Release -- split --file numbers.txt --out parts --count 50000000
        dotnet run -c Release -- 1a --file numbers.txt --threads 4
        dotnet run -c Release -- 1b --file numbers.txt --mode unsafe
        dotnet run -c Release -- 1b --file numbers.txt --mode lock
        dotnet run -c Release -- 1b --file numbers.txt --mode local
        dotnet run -c Release -- 1c --file numbers.txt --threads 4
        dotnet run -c Release -- 2 --dir E:\parts --threads 2
        Optional: --csv results/results.csv; --count N (small smoke tests only).
        Launch each configuration as a separate process for meaningful peak memory.
        """);
}
