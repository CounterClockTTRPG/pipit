using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vosk;

namespace Pipit.App.Audio;

/// <summary>
/// Hears note names for Note reading, offline, with a small Vosk model per language
/// (scripts/fetch-speech-models.sh puts them in assets/speech/). Listens to the same microphone
/// samples as everything else, restricted to a grammar of note names, and reports each word
/// with the capture-clock time it started, so names can be graded on the beat. Not on Android:
/// Vosk's .NET package has no Android library, so tablets name notes by tapping.
///
/// The models are not Godot resources (assets/speech has a .gdignore: scanning 200 MB of model
/// files made the editor start take minutes). Running from the project they are read where
/// they are; an exported build has them in a speech folder next to the program.
/// </summary>
public sealed class SpeechListener : IDisposable
{
    private const float ModelRate = 16000f;
    private static readonly Dictionary<string, Model> Models = new();

    private readonly AudioHub _hub;
    private readonly Model _model;
    private readonly string _grammar;
    private VoskRecognizer _recognizer;
    private readonly List<(string Word, double Time)> _words = new();
    private double _streamStart = double.NaN;
    private double _resamplePos;
    private float _lastSample;

    /// <summary>What is being heard right now (not final), for the screen.</summary>
    public string Partial { get; private set; } = "";

    /// <summary>Every word so far, finished ones and the ones still being heard, in order.</summary>
    public List<string> WordsSoFar
    {
        get
        {
            var words = _words.ConvertAll(w => w.Word);
            words.AddRange(Partial.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return words;
        }
    }

    private SpeechListener(AudioHub hub, Model model, string grammar)
    {
        _hub = hub;
        _model = model;
        _grammar = grammar;
        _recognizer = NewRecognizer();
    }

    private static string ModelFolder(string language) => OS.HasFeature("editor")
        ? ProjectSettings.GlobalizePath($"res://assets/speech/{language}")
        : Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "speech", language);

    /// <summary>Whether speech can be used here for this language.</summary>
    public static bool Available(string language) =>
        !Services.IsAndroid && File.Exists(Path.Combine(ModelFolder(language), "am", "final.mdl"));

    private static bool _resolverSet;

    /// <summary>
    /// Under Godot, .NET does not look for native libraries next to the assemblies, and Windows
    /// would not find libvosk's own dependencies there either: load them all by full path.
    /// </summary>
    private static void LoadNativeLibraries()
    {
        if (_resolverSet)
            return;
        _resolverSet = true;
        // Assemblies may be loaded from memory (no Location): try the build folders too.
        var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        var candidates = new[]
        {
            Path.GetDirectoryName(typeof(Model).Assembly.Location) ?? "",
            ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug"),
            ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/ExportRelease"),
            Path.Combine(exeDir, $"data_{ProjectSettings.GetSetting("application/config/name")}_windows_x86_64"),
            exeDir,
        };
        var dir = Array.Find(candidates, d => d != "" && (File.Exists(Path.Combine(d, "libvosk.dll")) || File.Exists(Path.Combine(d, "libvosk.so")))) ?? "";
        IntPtr vosk = IntPtr.Zero;
        foreach (var name in new[] { "libwinpthread-1.dll", "libgcc_s_seh-1.dll", "libstdc++-6.dll", "libvosk.dll", "libvosk.so", "libvosk.dylib" })
            if (File.Exists(Path.Combine(dir, name)))
            {
                var handle = NativeLibrary.Load(Path.Combine(dir, name));
                if (name.StartsWith("libvosk"))
                    vosk = handle;
            }
        NativeLibrary.SetDllImportResolver(typeof(Model).Assembly, (library, _, _) => library == "libvosk" ? vosk : IntPtr.Zero);
        // Only now: this is the first call into the library.
        Vosk.Vosk.SetLogLevel(-1);
    }

    /// <summary>Loads (once) the model for a language, in the background: a second or two.</summary>
    public static async Task<SpeechListener?> Create(AudioHub hub, string language, string grammar)
    {
        if (!Available(language))
            return null;
        if (!Models.TryGetValue(language, out var model))
        {
            Preload(language);
            try
            {
                model = await Loading[language];
            }
            finally
            {
                Loading.Remove(language); // a failed load is tried afresh next time
            }
            Models[language] = model;
        }
        return new SpeechListener(hub, model, grammar);
    }

    /// <summary>
    /// Running from the project, the model sits on the WSL share, where Vosk takes ~17 s to read
    /// it (under 1 s from a local disk): copy it once to user://speech-cache and use that.
    /// </summary>
    private static string LocalCopy(string folder, string language)
    {
        if (!folder.StartsWith(@"\\") && !folder.StartsWith("//"))
            return folder;
        var local = Path.Combine(ProjectSettings.GlobalizePath("user://speech-cache"), language);
        var marker = Path.Combine(local, ".complete");
        if (!File.Exists(marker))
        {
            if (Directory.Exists(local))
                Directory.Delete(local, true);
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(local, Path.GetRelativePath(folder, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            File.WriteAllText(marker, "");
        }
        return local;
    }

    /// <summary>Starts loading a language's model in the background, so it is ready by Start.</summary>
    public static void Preload(string language)
    {
        if (!Available(language) || Models.ContainsKey(language) || Loading.ContainsKey(language))
            return;
        LoadNativeLibraries();
        var folder = ModelFolder(language);
        Loading[language] = Task.Run(() => new Model(LocalCopy(folder, language)));
    }

    private static readonly Dictionary<string, Task<Model>> Loading = new();

    private VoskRecognizer NewRecognizer()
    {
        var recognizer = new VoskRecognizer(_model, ModelRate, _grammar);
        recognizer.SetWords(true);
        return recognizer;
    }

    public void Start()
    {
        _words.Clear();
        Partial = "";
        _streamStart = double.NaN;
        _resamplePos = 0;
        // A fresh recognizer for every session: word times count from the recognizer's first
        // sample, and Reset() does not restart them, so a second session's times came out late
        // by the length of the first (every name then missed its beat).
        _recognizer.Dispose();
        _recognizer = NewRecognizer();
        _hub.SamplesCaptured += OnSamples;
    }

    /// <summary>Stops listening; every word heard, with the capture time it began.</summary>
    public List<(string Word, double Time)> Stop()
    {
        _hub.SamplesCaptured -= OnSamples;
        Collect(_recognizer.FinalResult());
        return new List<(string, double)>(_words);
    }

    private void OnSamples(float[] samples, double time)
    {
        if (double.IsNaN(_streamStart))
            _streamStart = time;
        // Linear resampling to 16 kHz, carried over between buffers.
        var step = _hub.SampleRate / ModelRate;
        var output = new List<short>(samples.Length / 2);
        var pos = _resamplePos;
        while (pos < samples.Length)
        {
            var i = (int)pos;
            var a = i == 0 ? _lastSample : samples[i - 1];
            var b = samples[i];
            var frac = (float)(pos - i);
            output.Add((short)Math.Clamp((a + (b - a) * frac) * short.MaxValue, short.MinValue, short.MaxValue));
            pos += step;
        }
        _resamplePos = pos - samples.Length;
        _lastSample = samples[^1];
        var pcm = output.ToArray();
        if (_recognizer.AcceptWaveform(pcm, pcm.Length))
        {
            Collect(_recognizer.Result());
            Partial = "";
        }
        else
            Partial = JsonDocument.Parse(_recognizer.PartialResult()).RootElement.GetProperty("partial").GetString() ?? "";
    }

    /// <summary>Takes the words of a finished stretch of speech, with their start times.</summary>
    private void Collect(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var words))
            return;
        foreach (var w in words.EnumerateArray())
            _words.Add((w.GetProperty("word").GetString() ?? "", _streamStart + w.GetProperty("start").GetDouble()));
    }

    public void Dispose()
    {
        _hub.SamplesCaptured -= OnSamples;
        _recognizer.Dispose();
    }
}
