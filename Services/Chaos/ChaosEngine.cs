using System.IO;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class ChaosEngine
{
    private readonly string _projectRoot;
    private readonly ChaosQueueState _queue;
    public ChaosEngine(string projectRoot, ChaosQueueState queue) { _projectRoot = projectRoot; _queue = queue; }

    internal async Task<ChaosPreparedRun> PrepareAsync(ChaosOptions options, IProgress<ChaosProgress> progress, CancellationToken token = default)
    {
        Validate(options);
        return await Task.Run(() =>
        {
            progress.Report(new ChaosProgress("copying source inputs into staging", 0));
            var plan = ChaosRunPlan.Create(_projectRoot, token, SourceTrees(options), progress);
            try
            {
                plan.SettingsJson = System.Text.Json.JsonSerializer.Serialize(options);
                var queue = new ChaosQueueState { IsRunning = _queue.IsRunning, IsAccumulating = _queue.IsAccumulating };
                foreach (string path in _queue.FilesModified) queue.FilesModified.Add(plan.Target(path));
                foreach (var item in _queue.Macros) queue.Macros.Add(item.Key, item.Value);
                queue.GameLogicModes.UnionWith(_queue.GameLogicModes);
                var json = System.Text.Json.JsonSerializer.SerializeToNode(options)!.AsObject();
                json["SelectedM64Files"] = System.Text.Json.JsonSerializer.SerializeToNode((options.TargetMusic && options.SelectSpecificM64 ? options.SelectedM64Files : new List<string>()).Select(plan.Target).ToList());
                json["SfxReplacementRules"] = System.Text.Json.JsonSerializer.SerializeToNode((options.ReplaceSfx ? options.SfxReplacementRules : new List<ChaosReplacement>()).Select(r => new ChaosReplacement(plan.Target(r.TargetPath), plan.Import(r.ReplacementPath, token))).ToList());
                json["TextureReplacementRules"] = System.Text.Json.JsonSerializer.SerializeToNode((options.ReplaceTexturesCustom ? options.TextureReplacementRules : new List<ChaosReplacement>()).Select(r => new ChaosReplacement(plan.Target(r.TargetPath), plan.Import(r.ReplacementPath, token))).ToList());
                var staged = System.Text.Json.JsonSerializer.Deserialize<ChaosOptions>(json)!;
                var random = new Random(options.Seed);
                var checkedProgress = new CheckedProgress(progress, token);
                new ChaosAssetPreparation(plan.StageRoot, random, queue).Prepare(staged, checkedProgress, token);
                token.ThrowIfCancellationRequested();
                new ChaosSourcePatchService(plan.StageRoot, random, queue).Apply(staged, token);
                token.ThrowIfCancellationRequested();
                new ChaosTextProcessor(plan.StageRoot, random, queue).Apply(staged, checkedProgress, token);
                var files = new ChaosTargetCollector(plan.StageRoot, random, queue).Collect(staged, checkedProgress, token);
                new ChaosFileProcessor(random, queue).Apply(files, staged, checkedProgress, token, plan.Diagnostics);
                if (staged.RandomizeDl && staged.DlMode == 13 && staged.IntensityFor("display lists") > 0)
                    ModerateFacelessPatch.Apply(plan.StageRoot, queue);
                plan.Seal(token);
                var paths = queue.FilesModified.Select(p => Path.Combine(_projectRoot, Path.GetRelativePath(plan.StageRoot, p))).ToArray();
                queue.FilesModified.Clear(); queue.FilesModified.UnionWith(paths);
                return new ChaosPreparedRun(plan, queue);
            }
            catch { plan.Dispose(); throw; }
        }, token);
    }

    internal async Task<ChaosRunResult> CommitAsync(ChaosPreparedRun prepared, CancellationToken token = default)
    {
        var result = await Task.Run(() => prepared.Plan.Commit(token));
        if (result.Status == ChaosRunStatus.Completed)
        {
            _queue.FilesModified.Clear(); _queue.FilesModified.UnionWith(prepared.Queue.FilesModified);
            _queue.Macros.Clear(); foreach (var item in prepared.Queue.Macros) _queue.Macros.Add(item.Key, item.Value);
            _queue.GameLogicModes.Clear(); _queue.GameLogicModes.UnionWith(prepared.Queue.GameLogicModes);
        }
        return result;
    }

    public async Task ExecuteAsync(ChaosOptions options, IProgress<ChaosProgress> progress)
    {
        using var prepared = await PrepareAsync(options, progress);
        var result = await CommitAsync(prepared);
        if (result.Status != ChaosRunStatus.Completed) throw new IOException(result.Message);
    }

    internal IEnumerable<string> SourceTrees(ChaosOptions o)
    {
        yield return "include";
        yield return "src";
        if (o.RandomizeDl && o.DlMode == 13) { yield return "actors"; yield return "levels/castle_grounds"; }
        if (o.RandomizeSkybox || o.DlExclusion || ((o.TargetModels || o.RandomizeDl || o.RandomizeCollision) && o.SelectedLevel == "All Levels"))
        { yield return "levels"; yield return "leveleditor"; }
        else if (o.TargetModels || o.RandomizeDl || o.RandomizeCollision)
        { yield return Path.Combine("levels", o.SelectedLevel); yield return Path.Combine("leveleditor", "levels", o.SelectedLevel); }
        if (o.RandomizeTextures || o.DlExclusion || ((o.TargetModels || o.RandomizeDl) && o.SelectedLevel == "All Levels")) yield return "actors";
        else if (o.RandomizeMarioColors) yield return Path.Combine("actors", "mario");
        if (o.TargetSounds || o.ShuffleSounds) yield return "sound";
        else if (o.TargetMusic)
        {
            if (o.SelectSpecificM64)
                foreach (string file in o.SelectedM64Files)
                {
                    string relative = Path.GetRelativePath(_projectRoot, file);
                    yield return relative;
                    yield return relative + ".bak";
                }
            else yield return Path.Combine("sound", "sequences");
        }
        if (o.RandomizeTextures) yield return "textures";
        if (o.GlitchAnimations) yield return "assets";
        if (o.RandomizeText) yield return "text";
        foreach (var target in (o.ReplaceSfx ? o.SfxReplacementRules : new()).Concat(o.ReplaceTexturesCustom ? o.TextureReplacementRules : new()))
            yield return Path.GetRelativePath(_projectRoot, target.TargetPath).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        foreach (string file in _queue.FilesModified)
        {
            string relative = Path.GetRelativePath(_projectRoot, file);
            yield return relative;
            yield return relative + ".bak";
        }
    }

    private static void Validate(ChaosOptions o)
    {
        if (!double.IsFinite(o.Intensity) || o.Intensity < 0 || o.Intensity > 500) throw new InvalidDataException("intensity must be 0-500");
        foreach (var pair in o.IntensityOverrides)
            if (!ChaosTuning.Tools.Contains(pair.Key) || !double.IsFinite(pair.Value) || pair.Value < 0 || pair.Value > 500)
                throw new InvalidDataException("invalid tool intensity: " + pair.Key);
        if (o.RandomizeDl && (o.DlMode < 0 || o.DlMode > 13)) throw new InvalidDataException("unknown display list mode");
        o.MusicSettings.Validate();
        if (o.SelectedSfxModes.Any(m => m < 0 || m > SfxSampleEffects.Names.Length)) throw new InvalidDataException("unknown sound effect mode");
        if (o.ShuffleSounds && o.ExcludeSfx && (o.ExcludeInstruments || o.ShuffleSfxOnly))
            throw new InvalidDataException("sound filters exclude every sample; choose instruments or sound effects");
        if (o.ShuffleSounds && !o.SfxIdentityShuffle && !o.SfxPitchVariation && !o.SelectedSfxModes.Any(m => m != 0))
            throw new InvalidDataException("select a sample effect, pitch variation or identity shuffle");
        if (o.GameLogicRandomizer && (o.ActiveGameLogicModes.Count == 0 || o.ActiveGameLogicModes.Any(m => m < 1 || m > 20)))
            throw new InvalidDataException("select gameplay modes from 1 to 20");
        if (o.AudioDistortion && !(o.AlienSound || o.WackierSounds || o.WackyInstruments || o.AdsrScrambler || o.VibratoPitch || o.DmaBitcrusher))
            throw new InvalidDataException("select an available audio distortion patch");
        if (o.GameLogicCycleFrames is < 1 or > 3600) throw new InvalidDataException("cycle duration must be 1-3600 frames");
        if (o.WackierSounds || o.WackyInstruments || o.AdsrScrambler || o.VibratoPitch || o.DmaBitcrusher)
            throw new InvalidDataException("selected audio distortion patches are not implemented; use patch 0 only");
        if (o.TargetMusic && o.M64KeepStructure && o.M64Modes.Contains(27)) throw new InvalidDataException("M64 pointer mutation is unavailable");
        if (!(o.TargetMusic || o.TargetSounds || o.ShuffleSounds || o.RandomizeDl || o.RandomizeCollision || o.TargetModels || o.TargetGoddard ||
            o.RandomizeMarioColors || o.RandomizeSkybox || o.RandomizeText || o.RandomizeTextures || o.GlitchAnimations || o.GlitchHud ||
            o.ReplaceSfx || o.ReplaceTexturesCustom || o.JumpWeird || o.JumpDeath || o.LimboMario || o.AudioDistortion || o.GameLogicRandomizer ||
            o.ScrambleTitleScreen || o.RandomizeCutsceneCamera || o.LakituCameraChaos || o.StartLevelChaos || o.DlExclusion))
            throw new InvalidDataException("select at least one chaos target");
    }

    private sealed class CheckedProgress(IProgress<ChaosProgress> inner, CancellationToken token) : IProgress<ChaosProgress>
    {
        public void Report(ChaosProgress value) { token.ThrowIfCancellationRequested(); inner.Report(value); }
    }
}

internal sealed record ChaosPreparedRun(ChaosRunPlan Plan, ChaosQueueState Queue) : IDisposable
{
    public void Dispose() => Plan.Dispose();
}
