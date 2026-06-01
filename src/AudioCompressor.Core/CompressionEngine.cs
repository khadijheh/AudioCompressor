using System.Diagnostics;
using AudioCompressor.Core.Algorithms;
using AudioCompressor.Core.Logging;
using AudioCompressor.Core.Models;
using AudioCompressor.Core.Services;

namespace AudioCompressor.Core;

public class CompressionEngine
{
    private readonly IWavService _wavService;
    private readonly AsyncLogger _logger;

    private static readonly Dictionary<AlgorithmType, ICompressionAlgorithm> Algorithms = new()
    {
        [AlgorithmType.NonlinearQuantization] = new NonlinearQuantization(),
        [AlgorithmType.DPCM] = new DPCM(),
        [AlgorithmType.DeltaModulation] = new DeltaModulation()
    };

    public CompressionEngine(IWavService wavService, AsyncLogger logger)
    {
        _wavService = wavService;
        _logger = logger;
    }

    public CompressionResult Compress(string inputPath, CompressionConfig config,
        CancellationToken ct = default, IProgress<double>? progress = null,
        Action<string>? uiLog = null)
    {
        void Log(string msg)
        {
            _logger.Log(msg);
            uiLog?.Invoke(msg);
        }

        var sw = Stopwatch.StartNew();
        var result = new CompressionResult
        {
            OriginalPath = inputPath,
            FileName = Path.GetFileName(inputPath),
            Config = config,
            AlgorithmName = Algorithms[config.Algorithm].Name,
            OriginalSize = new FileInfo(inputPath).Length
        };

        Log($"=== Starting {result.AlgorithmName} ===");
        Log($"File: {result.FileName}");
        Log($"Config: algo={config.Algorithm}, bits={config.TargetBitsPerSample}, " +
            $"step={config.StepSize:F4}, adaptive={config.UseAdaptiveDelta}");

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.0);

        var info = _wavService.ReadFileInfo(inputPath);
        Log($"WAV: {info.SampleRate}Hz, {info.Channels}ch, {info.BitsPerSample}bit, {info.Duration.TotalSeconds:F2}s");

        var samples = _wavService.ReadSamples(inputPath);
        Log($"Loaded {samples.Length} float samples");
        progress?.Report(0.05);

        int originalDataBytes = samples.Length * (info.BitsPerSample / 8);

        ct.ThrowIfCancellationRequested();

        var algorithm = Algorithms[config.Algorithm];
        Log($"Running {algorithm.Name} compression...");

        var compressionProgress = new Progress<double>(p =>
        {
            progress?.Report(0.05 + p * 0.75);
            var elapsed = sw.Elapsed.TotalSeconds;
            var processed = (int)(p * samples.Length);
            var speed = elapsed > 0 ? processed / elapsed : 0;
            Log($"Progress: {p * 100:F0}%, speed: {speed:N0} samples/sec");
        });

        var compressed = algorithm.Compress(samples, config, compressionProgress, ct);
        Log($"Compressed: {compressed.Length} bytes ({compressed.Length * 8} bits)");

        ct.ThrowIfCancellationRequested();

        double ratio = (double)originalDataBytes / compressed.Length;
        Log($"Ratio: {ratio:F3}x, savings: {(1.0 - (double)compressed.Length / originalDataBytes) * 100:F1}%");

        progress?.Report(0.85);
        Log($"Decompressing...");

        var decompressionProgress = new Progress<double>(p =>
        {
            progress?.Report(0.85 + p * 0.10);
        });

        var reconstructed = algorithm.Decompress(compressed, config, samples.Length, decompressionProgress, ct);

        // keep compressed bytes in memory for session use
        result.CompressedBytes = compressed;
        result.OriginalSampleCount = samples.Length;

        // save compressed binary to disk (.comp) with a small header containing metadata
        var compDir = Path.Combine(Path.GetDirectoryName(inputPath) ?? ".", "compressed_output");
        Directory.CreateDirectory(compDir);
        var compName = Path.GetFileNameWithoutExtension(inputPath) + $"[{config.Algorithm}-{config.TargetBitsPerSample}bit].comp";
        var compPath = Path.Combine(compDir, compName);

        using (var fs = File.Create(compPath))
        using (var bw = new BinaryWriter(fs))
        {
            // Header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("ACMP")); // magic
            bw.Write(1); // version
            bw.Write((int)config.Algorithm);
            bw.Write(config.TargetBitsPerSample);
            bw.Write(config.StepSize);
            bw.Write(config.UseAdaptiveDelta);
            bw.Write(config.MuLawMu);
            bw.Write(config.ALawA);
            bw.Write(config.PredictorOrder);
            bw.Write(info.SampleRate);
            bw.Write(info.Channels);
            bw.Write(info.BitsPerSample);
            bw.Write(samples.Length);
            bw.Write(compressed.Length);
            // Data
            bw.Write(compressed);
        }

        Log($"Compressed binary saved: {compPath}");
        result.CompressedFilePath = compPath;

        var outputDir = Path.Combine(Path.GetDirectoryName(inputPath) ?? ".", "compressed_output");
        Directory.CreateDirectory(outputDir);
        var outputName = $"{Path.GetFileNameWithoutExtension(inputPath)}" +
                         $"[{config.Algorithm}-{config.TargetBitsPerSample}bit]" +
                         $".wav";
        var outputPath = Path.Combine(outputDir, outputName);

        var outputInfo = new WavFileInfo
        {
            FilePath = outputPath,
            FileSize = 0,
            Duration = info.Duration,
            SampleRate = info.SampleRate,
            Channels = info.Channels,
            BitRate = info.BitRate,
            Encoding = "PCM",
            BitsPerSample = info.BitsPerSample,
            DataSize = info.DataSize
        };

        progress?.Report(0.96);
        _wavService.WriteFile(outputPath, reconstructed, outputInfo);
        Log($"Output: {outputPath}");

        sw.Stop();
        result.CompressedDataSize = compressed.Length;
        result.DecompressedWavSize = new FileInfo(outputPath).Length;
        result.Elapsed = sw.Elapsed;
        result.OutputPath = outputPath;

        Log($"=== Done in {sw.Elapsed.TotalSeconds:F3}s ===");
        progress?.Report(1.0);

        return result;
    }

    public CompressionResult DecompressFromResult(CompressionResult result, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        if (result.CompressedBytes == null)
            throw new InvalidOperationException("No compressed data available in result.");

        var sw = Stopwatch.StartNew();
        var algo = Algorithms[result.Config.Algorithm];

        var decompressionProgress = new Progress<double>(p => progress?.Report(p));
        var reconstructed = algo.Decompress(result.CompressedBytes, result.Config, result.OriginalSampleCount, decompressionProgress, ct);

        // write file next to original with suffix _decompressed.wav
        var outputPath = Path.Combine(Path.GetDirectoryName(result.OriginalPath) ?? ".", Path.GetFileNameWithoutExtension(result.FileName) + "_decompressed.wav");
        var outputInfo = new WavFileInfo
        {
            FilePath = outputPath,
            FileSize = 0,
            Duration = TimeSpan.Zero,
            SampleRate = 44100,
            Channels = 1,
            BitRate = 0,
            Encoding = "PCM",
            BitsPerSample = (short)result.Config.TargetBitsPerSample,
            DataSize = reconstructed.Length * (result.Config.TargetBitsPerSample / 8)
        };

        _wavService.WriteFile(outputPath, reconstructed, outputInfo);

        sw.Stop();
        result.OutputPath = outputPath;
        result.Elapsed = sw.Elapsed;
        return result;
    }

    public CompressionResult DecompressCompressedFile(string compressedFilePath, string outputWavPath, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        if (!File.Exists(compressedFilePath))
            throw new FileNotFoundException("Compressed file not found", compressedFilePath);

        using var fs = File.OpenRead(compressedFilePath);
        using var br = new BinaryReader(fs);

        var magic = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
        if (magic != "ACMP")
            throw new InvalidDataException("Not a valid compressed file.");

        var version = br.ReadInt32();
        var algoInt = br.ReadInt32();
        var targetBits = br.ReadInt32();
        var stepSize = br.ReadDouble();
        var useAdaptive = br.ReadBoolean();
        var mu = br.ReadDouble();
        var aLaw = br.ReadDouble();
        var predictorOrder = br.ReadInt32();
        var sampleRate = br.ReadInt32();
        var channels = br.ReadInt16();
        var bitsPerSample = br.ReadInt16();
        var originalSampleCount = br.ReadInt32();
        var compressedLength = br.ReadInt32();

        var compressed = br.ReadBytes(compressedLength);

        var config = new CompressionConfig
        {
            Algorithm = (AlgorithmType)algoInt,
            TargetBitsPerSample = targetBits,
            StepSize = stepSize,
            UseAdaptiveDelta = useAdaptive,
            MuLawMu = mu,
            ALawA = aLaw,
            PredictorOrder = predictorOrder
        };

        var algo = Algorithms[config.Algorithm];
        var decompressionProgress = new Progress<double>(p => progress?.Report(p));
        var reconstructed = algo.Decompress(compressed, config, originalSampleCount, decompressionProgress, ct);

        var outputInfo = new WavFileInfo
        {
            FilePath = outputWavPath,
            FileSize = 0,
            Duration = TimeSpan.Zero,
            SampleRate = sampleRate,
            Channels = channels,
            BitRate = sampleRate * channels * bitsPerSample,
            Encoding = "PCM",
            BitsPerSample = bitsPerSample,
            DataSize = reconstructed.Length * (bitsPerSample / 8)
        };

        _wavService.WriteFile(outputWavPath, reconstructed, outputInfo);

        var result = new CompressionResult
        {
            FileName = Path.GetFileName(outputWavPath),
            OriginalPath = compressedFilePath,
            OutputPath = outputWavPath,
            Config = config,
            AlgorithmName = Algorithms[config.Algorithm].Name,
            CompressedDataSize = compressedLength,
            DecompressedWavSize = new FileInfo(outputWavPath).Length,
            Elapsed = TimeSpan.Zero,
            CompressedFilePath = compressedFilePath,
            OriginalSampleCount = originalSampleCount
        };

        return result;
    }

    public CompressionResult CompressSamples(float[] samples, WavFileInfo info, CompressionConfig config,
        CancellationToken ct = default, IProgress<double>? progress = null,
        Action<string>? uiLog = null)
    {
        void Log(string msg)
        {
            _logger.Log(msg);
            uiLog?.Invoke(msg);
        }

        var sw = Stopwatch.StartNew();
        var result = new CompressionResult
        {
            OriginalPath = info.FilePath ?? ".",
            FileName = Path.GetFileName(info.FilePath),
            Config = config,
            AlgorithmName = Algorithms[config.Algorithm].Name,
            OriginalSize = new FileInfo(info.FilePath ?? ".").Length
        };

        Log($"=== Starting {result.AlgorithmName} (memory samples) ===");
        Log($"File: {result.FileName}");
        Log($"Config: algo={config.Algorithm}, bits={config.TargetBitsPerSample}, step={config.StepSize:F4}, adaptive={config.UseAdaptiveDelta}");

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.0);

        Log($"Samples: {samples.Length} float samples");
        progress?.Report(0.05);

        int originalDataBytes = samples.Length * (info.BitsPerSample / 8);

        ct.ThrowIfCancellationRequested();

        var algorithm = Algorithms[config.Algorithm];
        Log($"Running {algorithm.Name} compression...");

        var compressionProgress = new Progress<double>(p =>
        {
            progress?.Report(0.05 + p * 0.75);
            var elapsed = sw.Elapsed.TotalSeconds;
            var processed = (int)(p * samples.Length);
            var speed = elapsed > 0 ? processed / elapsed : 0;
            Log($"Progress: {p * 100:F0}%, speed: {speed:N0} samples/sec");
        });

        var compressed = algorithm.Compress(samples, config, compressionProgress, ct);
        Log($"Compressed: {compressed.Length} bytes ({compressed.Length * 8} bits)");

        ct.ThrowIfCancellationRequested();

        double ratio = (double)originalDataBytes / compressed.Length;
        Log($"Ratio: {ratio:F3}x, savings: {(1.0 - (double)compressed.Length / originalDataBytes) * 100:F1}%");

        progress?.Report(0.85);
        Log($"Decompressing...");

        var decompressionProgress = new Progress<double>(p =>
        {
            progress?.Report(0.85 + p * 0.10);
        });

        var reconstructed = algorithm.Decompress(compressed, config, samples.Length, decompressionProgress, ct);

        // keep compressed bytes in memory for session use
        result.CompressedBytes = compressed;
        result.OriginalSampleCount = samples.Length;

        // save compressed binary to disk (.comp) with a small header containing metadata
        var compDir = Path.Combine(Path.GetDirectoryName(info.FilePath) ?? ".", "compressed_output");
        Directory.CreateDirectory(compDir);
        var compName = Path.GetFileNameWithoutExtension(info.FilePath) + $"[{config.Algorithm}-{config.TargetBitsPerSample}bit].comp";
        var compPath = Path.Combine(compDir, compName);

        using (var fs = File.Create(compPath))
        using (var bw = new BinaryWriter(fs))
        {
            // Header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("ACMP")); // magic
            bw.Write(1); // version
            bw.Write((int)config.Algorithm);
            bw.Write(config.TargetBitsPerSample);
            bw.Write(config.StepSize);
            bw.Write(config.UseAdaptiveDelta);
            bw.Write(config.MuLawMu);
            bw.Write(config.ALawA);
            bw.Write(config.PredictorOrder);
            bw.Write(info.SampleRate);
            bw.Write(info.Channels);
            bw.Write(info.BitsPerSample);
            bw.Write(samples.Length);
            bw.Write(compressed.Length);
            // Data
            bw.Write(compressed);
        }

        Log($"Compressed binary saved: {compPath}");
        result.CompressedFilePath = compPath;

        var outputDir = Path.Combine(Path.GetDirectoryName(info.FilePath) ?? ".", "compressed_output");
        Directory.CreateDirectory(outputDir);
        var outputName = $"{Path.GetFileNameWithoutExtension(info.FilePath)}" +
                         $"[{config.Algorithm}-{config.TargetBitsPerSample}bit]" +
                         $".wav";
        var outputPath = Path.Combine(outputDir, outputName);

        var outputInfo = new WavFileInfo
        {
            FilePath = outputPath,
            FileSize = 0,
            Duration = info.Duration,
            SampleRate = info.SampleRate,
            Channels = info.Channels,
            BitRate = info.BitRate,
            Encoding = "PCM",
            BitsPerSample = info.BitsPerSample,
            DataSize = info.DataSize
        };

        progress?.Report(0.96);
        _wavService.WriteFile(outputPath, reconstructed, outputInfo);
        Log($"Output: {outputPath}");

        sw.Stop();
        result.CompressedDataSize = compressed.Length;
        result.DecompressedWavSize = new FileInfo(outputPath).Length;
        result.Elapsed = sw.Elapsed;
        result.OutputPath = outputPath;

        Log($"=== Done in {sw.Elapsed.TotalSeconds:F3}s ===");
        progress?.Report(1.0);

        return result;
    }

    public CompressionResult ReadCompressedMetadata(string compressedFilePath)
    {
        if (!File.Exists(compressedFilePath))
            throw new FileNotFoundException("Compressed file not found", compressedFilePath);

        using var fs = File.OpenRead(compressedFilePath);
        using var br = new BinaryReader(fs);

        var magic = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
        if (magic != "ACMP")
            throw new InvalidDataException("Not a valid compressed file.");

        var version = br.ReadInt32();
        var algoInt = br.ReadInt32();
        var targetBits = br.ReadInt32();
        var stepSize = br.ReadDouble();
        var useAdaptive = br.ReadBoolean();
        var mu = br.ReadDouble();
        var aLaw = br.ReadDouble();
        var predictorOrder = br.ReadInt32();
        var sampleRate = br.ReadInt32();
        var channels = br.ReadInt16();
        var bitsPerSample = br.ReadInt16();
        var originalSampleCount = br.ReadInt32();
        var compressedLength = br.ReadInt32();

        var result = new CompressionResult
        {
            FileName = Path.GetFileName(compressedFilePath),
            OriginalPath = compressedFilePath,
            CompressedFilePath = compressedFilePath,
            CompressedDataSize = compressedLength,
            OriginalSampleCount = originalSampleCount,
            Config = new CompressionConfig
            {
                Algorithm = (AlgorithmType)algoInt,
                TargetBitsPerSample = targetBits,
                StepSize = stepSize,
                UseAdaptiveDelta = useAdaptive,
                MuLawMu = mu,
                ALawA = aLaw,
                PredictorOrder = predictorOrder
            }
        };

        return result;
    }
}
