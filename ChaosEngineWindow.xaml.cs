using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text;
using Sm64DecompLevelViewer.Services.Chaos;
using Sm64DecompLevelViewer.Services;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Sm64DecompLevelViewer
{
    public partial class ChaosEngineWindow : Window
    {
        private readonly string _projectRoot;
        private readonly ChaosQueueState _queueState = new();
        private readonly ChaosEngine _engine;
        private readonly Random _seedGenerator = new();
        private readonly List<SoundReplacerWindow.SoundReplacementRule> _sfxReplacementRules = new();
        private readonly List<TextureReplacerWindow.TextureReplacementRule> _textureReplacementRules = new();
        private readonly List<int> _activeM64Modes = new() { 0, 1, 2, 3, 4, 5 };
        private readonly List<int> _activeGameLogicModes = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
        private bool _gameLogicSimultaneous = true;
        private readonly List<int> _activeAudioDistortionModes = new() { 0 };

        private readonly string _presetsDir;
        private bool _isLoadingPreset = false;
        private List<string> _selectedTextures = new();

        private readonly List<ChaosPreset> _stockpile = new();
        private readonly List<ChaosPreset> _queue = new();
        private bool _isBusy;
        private Dictionary<string, double> _intensityOverrides = new();
        private M64MutationSettings _musicSettings = new();
        private int _gameLogicCycleFrames = 300;
        private CancellationTokenSource? _runCancellation;

        private void IntensityOverrides_Click(object sender, RoutedEventArgs e)
        {
            var settings = ChaosControls.EditIntensities(this, _intensityOverrides);
            if (settings != null) _intensityOverrides = settings;
        }

        private int[] GetSfxModes() => SfxRandomizerModeListBox.SelectedItems.Cast<string>()
            .Select(name => Array.IndexOf(SfxSampleEffects.Names, name) + 1).Order().ToArray();

        private void SetSfxModes(IEnumerable<int> modes)
        {
            SfxRandomizerModeListBox.UnselectAll();
            foreach (int mode in modes.Distinct())
            {
                if (mode < 0 || mode > SfxSampleEffects.Names.Length) throw new InvalidDataException("Unknown sound effect mode in preset.");
                if (mode > 0) SfxRandomizerModeListBox.SelectedItems.Add(SfxSampleEffects.Names[mode - 1]);
            }
        }

        public ChaosEngineWindow(string projectRoot)
        {
            InitializeComponent();
            foreach (string name in SfxSampleEffects.Names) SfxRandomizerModeListBox.Items.Add(name);
            Closing += (_, e) => { if (_isBusy) e.Cancel = true; };
            _projectRoot = projectRoot;
            _engine = new ChaosEngine(projectRoot, _queueState);
            
            _presetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Randm64", "Settings", "ChaosPresets");
            Directory.CreateDirectory(_presetsDir);

            ChaosButton.IsEnabled = false;
            Loaded += async (_, _) =>
            {
                await LoadProjectLevelsAndM64sAsync();
                if (!IsLoaded) return;
                LoadPresetsList();
                string defaultPath = Path.Combine(_presetsDir, "_default_settings.json");
                if (File.Exists(defaultPath)) LoadPresetFromFile(defaultPath);
                ChaosButton.IsEnabled = true;
            };
        }

        private async void ChaosButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;
            SetWindowEnabled(false);
            _queueState.IsRunning = false;
            _queueState.IsAccumulating = false;
            _queueState.FilesModified.Clear();
            _queueState.Macros.Clear();
            _queueState.GameLogicModes.Clear();

            ChaosButton.IsEnabled = false;
            CorruptionProgressBar.Visibility = Visibility.Visible;
            CorruptionProgressBar.Value = 0;
            StatusTextBlock.Text = "Scanning workspace for files...";

            try
            {
                await InflictChaosInternalAsync();
                StatusTextBlock.Text = "Chaos successfully inflicted! Rebuild project to see effects.";
                MessageBox.Show("Chaos successfully inflicted! Build the ROM via WSL to experience the glitched results.\n\nYou can revert all corruptions anytime using the standard Git checkout option.", "Chaos Inflicted", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Cancelled; current entry was not applied; earlier queue entries remain";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error running Chaos Engine.";
                MessageBox.Show($"Error running Chaos Engine: {ex.Message}", "Chaos Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isBusy = false;
                SetWindowEnabled(true);
                CorruptionProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task InflictChaosInternalAsync()
        {

            int seed = 0;
            bool useRandomSeed = UseRandomSeedCheck.IsChecked == true;
            string seedText = ChaosSeedTextBox.Text;

            if (useRandomSeed || string.IsNullOrWhiteSpace(seedText))
            {
                seed = _seedGenerator.Next();
                ChaosSeedTextBox.Text = seed.ToString();
            }
            else
            {
                if (!int.TryParse(seedText, out seed))
                {
                    seed = ChaosSeed.Resolve(seedText);
                }
            }

            double intensity = IntensifySlider.Value;
            bool targetMusic = TargetMusicNotesCheck.IsChecked == true;
            bool m64KeepStructure = M64KeepStructureRadio.IsChecked == true;
            bool targetSounds = TargetSoundsCheck.IsChecked == true;
            bool shuffleSounds = ShuffleSoundsCheck.IsChecked == true;
            bool excludeInstruments = ExcludeInstrumentsShuffleCheck.IsChecked == true;
            bool excludeSfx = ExcludeSfxShuffleCheck.IsChecked == true;
            bool shuffleSfxOnly = ShuffleSfxOnlyCheck.IsChecked == true;
            bool randomizeDl = RandomizeDlCheck.IsChecked == true;
            int dlMode = DlRandomizerModeComboBox.SelectedIndex;
            bool randomizeCollision = RandomizeCollisionCheck.IsChecked == true;
            int collisionMode = CollisionRandomizerModeComboBox.SelectedIndex;
            bool targetModels = TargetModelsCheck.IsChecked == true;
            bool targetGoddard = TargetGoddardCheck.IsChecked == true;
            int goddardMode = GoddardModeComboBox.SelectedIndex;
            int mode = ModeComboBox.SelectedIndex;
            bool randomizeMarioColors = RandomizeMarioColorsCheck.IsChecked == true;
            int marioColorsArea = MarioColorsAreaComboBox.SelectedIndex;
            int marioColorsMode = MarioColorsModeComboBox.SelectedIndex;

            bool randomizeSkybox = RandomizeSkyboxCheck.IsChecked == true;
            bool randomizeText = RandomizeTextCheck.IsChecked == true;
            int textMode = TextRandomizeModeComboBox.SelectedIndex;
            int[] m64Modes = _activeM64Modes.ToArray();
            bool randomizeTextures = RandomizeTexturesCheck.IsChecked == true;
            int textureMode = TextureRandomizeModeComboBox.SelectedIndex;
            
            bool glitchAnimations = GlitchAnimationsCheck.IsChecked == true;
            int animMode = AnimationGlitcherModeComboBox.SelectedIndex;
            bool glitchHud = GlitchHudCheck.IsChecked == true;
            int hudMode = HudGlitcherModeComboBox.SelectedIndex;

            bool replaceSfx = ReplaceSfxCheck.IsChecked == true;
            bool replaceTexturesCustom = ReplaceTexturesCustomCheck.IsChecked == true;

            bool jumpWeird = ChaosLogicJumpWeird.IsChecked == true;
            bool jumpDeath = ChaosLogicJumpDeath.IsChecked == true;
            bool limboMario = LimboMarioCheck.IsChecked == true;
            int limboMarioMode = LimboMarioModeComboBox.SelectedIndex;
            bool audioDistortion = AudioDistortionCheck.IsChecked == true;
            bool alienSound = audioDistortion && _activeAudioDistortionModes.Contains(0);
            bool wackierSounds = audioDistortion && _activeAudioDistortionModes.Contains(1);
            bool wackyInstruments = audioDistortion && _activeAudioDistortionModes.Contains(2);
            bool adsrScrambler = audioDistortion && _activeAudioDistortionModes.Contains(3);
            bool vibratoPitch = audioDistortion && _activeAudioDistortionModes.Contains(4);
            bool dmaBitcrusher = audioDistortion && _activeAudioDistortionModes.Contains(5);
            bool gameLogicRandomizer = GameLogicRandomizerCheck.IsChecked == true;

            bool scrambleTitleScreen = ScrambleTitleScreenCheck.IsChecked == true;
            int titleScramblerMode = TitleScreenScramblerModeComboBox.SelectedIndex;
            bool randomizeCutsceneCamera = RandomizeCutsceneCameraCheck.IsChecked == true;
            int cutsceneCameraMode = CutsceneCameraModeComboBox.SelectedIndex;
            bool lakituCameraChaos = LakituCameraChaosCheck.IsChecked == true;
            int lakituCameraMode = LakituCameraModeComboBox.SelectedIndex;
            bool startLevelChaos = StartLevelChaosCheck.IsChecked == true;
            string startLevelConstant = "LEVEL_CASTLE_GROUNDS";
            string[] startLevelConstants = {
                "LEVEL_BOB", "LEVEL_WF", "LEVEL_JRB", "LEVEL_CCM", "LEVEL_BBH", "LEVEL_HMC",
                "LEVEL_LLL", "LEVEL_SSL", "LEVEL_DDD", "LEVEL_SL", "LEVEL_WDW", "LEVEL_TTM",
                "LEVEL_THI", "LEVEL_TTC", "LEVEL_RR", "LEVEL_SA", "LEVEL_COTMC", "LEVEL_TOTWC",
                "LEVEL_BITDW", "LEVEL_BITFS", "LEVEL_BITS"
            };
            int startLevelIndex = StartLevelComboBox.SelectedIndex;
            if (startLevelIndex >= 0 && startLevelIndex < startLevelConstants.Length)
            {
                startLevelConstant = startLevelConstants[startLevelIndex];
            }

            string selectedLevel = (LevelSelectionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Levels";


            var options = new ChaosOptions
            {
                Seed = seed,
                IntensityOverrides = new(_intensityOverrides),
                MusicSettings = _musicSettings,
                GameLogicCycleFrames = _gameLogicCycleFrames,
                Intensity = intensity,
                TargetMusic = targetMusic,
                M64KeepStructure = m64KeepStructure,
                TargetSounds = targetSounds,
                ShuffleSounds = shuffleSounds,
                ExcludeInstruments = excludeInstruments,
                ExcludeSfx = excludeSfx,
                ShuffleSfxOnly = shuffleSfxOnly,
                RandomizeDl = randomizeDl,
                DlMode = dlMode,
                RandomizeCollision = randomizeCollision,
                CollisionMode = collisionMode,
                TargetModels = targetModels,
                TargetGoddard = targetGoddard,
                GoddardMode = goddardMode,
                Mode = mode,
                RandomizeMarioColors = randomizeMarioColors,
                MarioColorsArea = marioColorsArea,
                MarioColorsMode = marioColorsMode,
                RandomizeSkybox = randomizeSkybox,
                RandomizeText = randomizeText,
                TextMode = textMode,
                M64Modes = m64Modes,
                RandomizeTextures = randomizeTextures,
                TextureMode = textureMode,
                GlitchAnimations = glitchAnimations,
                AnimMode = animMode,
                GlitchHud = glitchHud,
                HudMode = hudMode,
                ReplaceSfx = replaceSfx,
                ReplaceTexturesCustom = replaceTexturesCustom,
                JumpWeird = jumpWeird,
                JumpDeath = jumpDeath,
                LimboMario = limboMario,
                LimboMarioMode = limboMarioMode,
                AudioDistortion = audioDistortion,
                AlienSound = alienSound,
                WackierSounds = wackierSounds,
                WackyInstruments = wackyInstruments,
                AdsrScrambler = adsrScrambler,
                VibratoPitch = vibratoPitch,
                DmaBitcrusher = dmaBitcrusher,
                GameLogicRandomizer = gameLogicRandomizer,
                ScrambleTitleScreen = scrambleTitleScreen,
                TitleScramblerMode = titleScramblerMode,
                RandomizeCutsceneCamera = randomizeCutsceneCamera,
                CutsceneCameraMode = cutsceneCameraMode,
                LakituCameraChaos = lakituCameraChaos,
                LakituCameraMode = lakituCameraMode,
                StartLevelChaos = startLevelChaos,
                StartLevelConstant = startLevelConstant,
                SelectedLevel = selectedLevel,
                DlExclusion = DlExclusionCheck.IsChecked == true,
                SfxMode = 0,
                SfxModes = GetSfxModes(),
                SfxIdentityShuffle = SfxIdentityShuffleCheck.IsChecked == true,
                SfxPitchVariation = SfxPitchVariationCheck.IsChecked == true,
                SelectSpecificM64 = M64SelectRadio.IsChecked == true,
                SelectedM64Files = M64SelectionListBox.SelectedItems.Cast<ListBoxItem>().Select(item => item.Tag).OfType<string>().ToList(),
                RandomizeSelectedOnly = TextureRandomizeSelectedRadio.IsChecked == true,
                ActiveGameLogicModes = _activeGameLogicModes.ToList(),
                GameLogicSimultaneous = _gameLogicSimultaneous,
                SelectedTextures = _selectedTextures.ToList(),
                SfxReplacementRules = _sfxReplacementRules.Select(r => new ChaosReplacement(r.TargetPath, r.ReplacementPath)).ToList(),
                TextureReplacementRules = _textureReplacementRules.Select(r => new ChaosReplacement(r.TargetPath, r.ReplacementPath)).ToList(),
            };
            using var cancellation = new CancellationTokenSource();
            _runCancellation = cancellation;
            var busy = ChaosControls.Busy(this, cancellation);
            try
            {
                using var prepared = await _engine.PrepareAsync(options, new Progress<ChaosProgress>(p =>
                {
                    if (p.Status != null) StatusTextBlock.Text = p.Status;
                    if (p.Percent.HasValue) CorruptionProgressBar.Value = p.Percent.Value;
                }), cancellation.Token);
                busy.Hide();
                cancellation.Token.ThrowIfCancellationRequested();
                if (!ChaosControls.Preview(this, prepared.Plan))
                {
                    prepared.Plan.CancelPreview(); SaveRunReport(prepared.Plan);
                    throw new OperationCanceledException();
                }
                busy.Show();
                var result = await _engine.CommitAsync(prepared, cancellation.Token);
                SaveRunReport(prepared.Plan);
                if (result.Status == ChaosRunStatus.Cancelled) throw new OperationCanceledException(result.Message);
                if (result.Status != ChaosRunStatus.Completed) throw new IOException(result.Status + ": " + result.Message + "\n" + result.RecoveryPath);

            }
            finally { busy.Tag = "finished"; busy.Close(); _runCancellation = null; }
        }

        private void SaveRunReport(ChaosRunPlan plan)
        {
            string report = Path.Combine(_presetsDir, "runs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
            try { plan.SaveReport(report); StatusTextBlock.Text = "Run report: " + report; }
            catch (Exception ex) { MessageBox.Show(this, "Could not save run report: " + ex.Message); }
        }

        private void IntensifySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IntensifyValueText == null) return;

            double val = e.NewValue;
            IntensifyValueText.Text = $"{val:F0}%";

            if (val <= 100)
            {
                IntensifyValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Green (#4CAF50)
                if (IntensifyWarningText != null) IntensifyWarningText.Visibility = Visibility.Collapsed;
            }
            else if (val <= 250)
            {
                IntensifyValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)); // Yellow/Amber (#FFC107)
                if (IntensifyWarningText != null) IntensifyWarningText.Visibility = Visibility.Collapsed;
            }
            else if (val <= 400)
            {
                IntensifyValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)); // Red (#F44336)
                if (IntensifyWarningText != null) IntensifyWarningText.Visibility = Visibility.Collapsed;
            }
            else
            {
                IntensifyValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(183, 28, 28)); // Dark Red (#B71C1C)
                if (IntensifyWarningText != null) IntensifyWarningText.Visibility = Visibility.Visible;
            }
        }

        private void ShuffleSoundsCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (ShuffleSoundsOptionsPanel != null)
            {
                ShuffleSoundsOptionsPanel.Visibility = ShuffleSoundsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeMarioColorsCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (MarioColorsOptionsPanel != null)
            {
                MarioColorsOptionsPanel.Visibility = RandomizeMarioColorsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TargetMusicNotesCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (MusicNotesOptionsPanel != null)
            {
                MusicNotesOptionsPanel.Visibility = TargetMusicNotesCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeTextCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (TextRandomizeOptionsPanel != null)
            {
                TextRandomizeOptionsPanel.Visibility = RandomizeTextCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeTexturesCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (TextureRandomizeOptionsPanel != null)
            {
                TextureRandomizeOptionsPanel.Visibility = RandomizeTexturesCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ScrambleTitleScreenCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (TitleScreenScramblerPanel != null)
            {
                TitleScreenScramblerPanel.Visibility = ScrambleTitleScreenCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeCutsceneCameraCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (CutsceneCameraPanel != null)
            {
                CutsceneCameraPanel.Visibility = RandomizeCutsceneCameraCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void LakituCameraChaosCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (LakituCameraChaosPanel != null)
            {
                LakituCameraChaosPanel.Visibility = LakituCameraChaosCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void LimboMarioCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (LimboMarioPanel != null)
            {
                LimboMarioPanel.Visibility = LimboMarioCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void StartLevelChaosCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (StartLevelChaosPanel != null)
            {
                StartLevelChaosPanel.Visibility = StartLevelChaosCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void GameLogicRandomizerCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (GameLogicRandomizerPanel != null)
            {
                GameLogicRandomizerPanel.Visibility = GameLogicRandomizerCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void AudioDistortionCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (AudioDistortionPanel != null)
            {
                AudioDistortionPanel.Visibility = AudioDistortionCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ConfigureAudioDistortionModes_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AudioDistortionWindow(_activeAudioDistortionModes) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _activeAudioDistortionModes.Clear();
                _activeAudioDistortionModes.AddRange(dialog.SelectedPatchIds);
                UpdateAudioDistortionSummaryText();
            }
        }

        private void UpdateAudioDistortionSummaryText()
        {
            if (ActiveAudioDistortionSummaryText != null)
            {
                ActiveAudioDistortionSummaryText.Text = $"Active patches: {_activeAudioDistortionModes.Count}";
            }
        }

        private void ConfigureGameLogicModes_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GameLogicModesWindow(_activeGameLogicModes, _gameLogicSimultaneous, _gameLogicCycleFrames) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _activeGameLogicModes.Clear();
                _activeGameLogicModes.AddRange(dialog.SelectedModeIds);
                _gameLogicSimultaneous = dialog.IsSimultaneous;
                _gameLogicCycleFrames = dialog.CycleFrames;
                UpdateGameLogicModesSummaryText();
            }
        }

        private void UpdateGameLogicModesSummaryText()
        {
            if (ActiveGameLogicModesSummaryText != null)
            {
                string method = _gameLogicSimultaneous ? "Simultaneous" : "Cycle";
                ActiveGameLogicModesSummaryText.Text = $"Active logic modes: {_activeGameLogicModes.Count} ({method})";
            }
        }

        private void TextureTarget_Changed(object sender, RoutedEventArgs e)
        {
            if (SelectTexturesButton != null && TextureRandomizeSelectedRadio != null)
            {
                SelectTexturesButton.Visibility = TextureRandomizeSelectedRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SelectTexturesButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new TextureSelectionWindow(_projectRoot, _selectedTextures) { Owner = this };
            if (window.ShowDialog() == true)
            {
                _selectedTextures = window.SelectedRelativePaths;
            }
        }

        private void ReplaceTexturesCustomCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (ReplaceTexturesCustomPanel != null)
            {
                ReplaceTexturesCustomPanel.Visibility = ReplaceTexturesCustomCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ConfigureTextureReplacer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextureReplacerWindow(_projectRoot, _textureReplacementRules) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _textureReplacementRules.Clear();
                _textureReplacementRules.AddRange(dialog.ActiveRules);
                ActiveTextureReplacementsSummaryText.Text = $"Active replacement rules: {_textureReplacementRules.Count}";
            }
        }

        private async Task LoadProjectLevelsAndM64sAsync()
        {
            try
            {
                var (levels, sequences) = await Task.Run(() =>
                {
                    string levelsDir = Path.Combine(_projectRoot, "levels");
                    if (!Directory.Exists(levelsDir)) levelsDir = Path.Combine(_projectRoot, "howtomake", "levels");
                    string[] levels = Directory.Exists(levelsDir) ? Directory.GetDirectories(levelsDir)
                        .Select(Path.GetFileName).Where(name => name != null && !name.StartsWith(".") && name != "src" && name != "include" && name != "bin").ToArray()! : [];
                    string seqDir = Path.Combine(_projectRoot, "sound", "sequences");
                    string[] sequences = Directory.Exists(seqDir) ? Directory.GetFiles(seqDir, "*.m64", SearchOption.AllDirectories)
                        .Where(file => !Path.GetFileName(file).Contains("00_sound_player") && !Path.GetFileName(file).Contains("sfx")).ToArray() : [];
                    return (levels, sequences);
                });
                if (!IsLoaded) return;
                foreach (string name in levels)
                    LevelSelectionComboBox.Items.Add(new ComboBoxItem { Content = name });
                foreach (string file in sequences)
                {
                    M64SelectionListBox.Items.Add(new ListBoxItem { Content = Path.GetFileName(file), Tag = file });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning project files on initialization: {ex.Message}");
            }
        }

        private void M64Radio_Checked(object sender, RoutedEventArgs e)
        {
            if (M64SelectionListBox != null)
            {
                M64SelectionListBox.Visibility = M64SelectRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ReplaceSfxCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (ReplaceSfxPanel != null)
            {
                ReplaceSfxPanel.Visibility = ReplaceSfxCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ConfigureSoundReplacer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SoundReplacerWindow(_projectRoot, _sfxReplacementRules) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _sfxReplacementRules.Clear();
                _sfxReplacementRules.AddRange(dialog.ActiveRules);
                ActiveReplacementsSummaryText.Text = $"Active replacement rules: {_sfxReplacementRules.Count}";
            }
        }

        private void ConfigureM64Modes_Click(object sender, RoutedEventArgs e)
        {
            int previewSeed = UseRandomSeedCheck.IsChecked == true ? new Random().Next() : ChaosSeed.Resolve(ChaosSeedTextBox.Text);
            var dialog = new M64CorruptionWindow(_activeM64Modes, _musicSettings, _projectRoot,
                _intensityOverrides.TryGetValue("M64", out double musicIntensity) ? musicIntensity : IntensifySlider.Value,
                previewSeed) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _activeM64Modes.Clear();
                _activeM64Modes.AddRange(dialog.SelectedModeIds);
                _musicSettings = dialog.MusicSettings;
                // Saving the mode picker opts into those effects for both preview and export.
                M64KeepStructureRadio.IsChecked = true;
            }
        }

        private void GlitchAnimationsCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (AnimationGlitcherPanel != null)
            {
                AnimationGlitcherPanel.Visibility = GlitchAnimationsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void GlitchHudCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (HudGlitcherPanel != null)
            {
                HudGlitcherPanel.Visibility = GlitchHudCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeDlCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (DlRandomizerPanel != null)
            {
                DlRandomizerPanel.Visibility = RandomizeDlCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void DlExclusionCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (DlExclusionPanel != null)
            {
                DlExclusionPanel.Visibility = DlExclusionCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeCollisionCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (CollisionRandomizerPanel != null)
            {
                CollisionRandomizerPanel.Visibility = RandomizeCollisionCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TargetGoddardCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (GoddardOptionsPanel != null)
            {
                GoddardOptionsPanel.Visibility = TargetGoddardCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void LoadPresetsList(string selectedPresetName = "")
        {
            _isLoadingPreset = true;
            try
            {
                PresetComboBox.Items.Clear();
                PresetComboBox.Items.Add("Default Settings");

                if (Directory.Exists(_presetsDir))
                {
                    var files = Directory.GetFiles(_presetsDir, "*.json");
                    foreach (var file in files)
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (!name.StartsWith("_"))
                        {
                            PresetComboBox.Items.Add(name);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(selectedPresetName))
                {
                    PresetComboBox.SelectedItem = selectedPresetName;
                }
                else
                {
                    PresetComboBox.SelectedIndex = 0;
                }
            }
            catch { }
            finally
            {
                _isLoadingPreset = false;
            }
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingPreset) return;
            if (IntensifySlider == null) return; // Guard for XAML init phase

            if (PresetComboBox.SelectedIndex <= 0)
            {
                // Reset to default settings
                _intensityOverrides.Clear(); _musicSettings = new(); _gameLogicCycleFrames = 300;
                IntensifySlider.Value = 10;
                LevelSelectionComboBox.SelectedIndex = 0;
                TargetMusicNotesCheck.IsChecked = true;
                _activeM64Modes.Clear();
                _activeM64Modes.AddRange(new[] { 0, 1, 2, 3, 4, 5 });
                M64AllRadio.IsChecked = true;
                M64KeepStructureRadio.IsChecked = true;
                TargetSoundsCheck.IsChecked = true;
                ShuffleSoundsCheck.IsChecked = false;
                ExcludeInstrumentsShuffleCheck.IsChecked = false;
                ExcludeSfxShuffleCheck.IsChecked = false;
                ShuffleSfxOnlyCheck.IsChecked = false;
                SfxIdentityShuffleCheck.IsChecked = true;
                SfxPitchVariationCheck.IsChecked = false;
                SfxRandomizerModeListBox.UnselectAll();
                ReplaceSfxCheck.IsChecked = false;
                RandomizeDlCheck.IsChecked = false;
                DlRandomizerModeComboBox.SelectedIndex = 0;
                DlExclusionCheck.IsChecked = false;
                TargetModelsCheck.IsChecked = true;
                TargetGoddardCheck.IsChecked = true;
                GoddardModeComboBox.SelectedIndex = 0;
                RandomizeMarioColorsCheck.IsChecked = false;
                MarioColorsAreaComboBox.SelectedIndex = 0;
                MarioColorsModeComboBox.SelectedIndex = 0;
                
                GlitchAnimationsCheck.IsChecked = false;
                AnimationGlitcherModeComboBox.SelectedIndex = 0;
                GlitchHudCheck.IsChecked = false;
                HudGlitcherModeComboBox.SelectedIndex = 0;
                RandomizeTexturesCheck.IsChecked = false;
                TextureRandomizeModeComboBox.SelectedIndex = 0;
                ReplaceTexturesCustomCheck.IsChecked = false;
                _textureReplacementRules.Clear();
                ActiveTextureReplacementsSummaryText.Text = "Active replacement rules: 0";
                TextureRandomizeAllRadio.IsChecked = true;
                _selectedTextures.Clear();
                ModeComboBox.SelectedIndex = 0;
                RandomizeSkyboxCheck.IsChecked = false;
                RandomizeTextCheck.IsChecked = false;
                TextRandomizeModeComboBox.SelectedIndex = 0;
                
                ChaosLogicJumpWeird.IsChecked = false;
                ChaosLogicJumpDeath.IsChecked = false;
                LimboMarioCheck.IsChecked = false;
                AudioDistortionCheck.IsChecked = false;
                _activeAudioDistortionModes.Clear();
                _activeAudioDistortionModes.Add(0);
                UpdateAudioDistortionSummaryText();
                GameLogicRandomizerCheck.IsChecked = false;
                _activeGameLogicModes.Clear();
                _activeGameLogicModes.AddRange(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 });
                _gameLogicSimultaneous = true;
                UpdateGameLogicModesSummaryText();
                return;
            }

            string presetName = PresetComboBox.SelectedItem.ToString();
            string filePath = Path.Combine(_presetsDir, presetName + ".json");
            LoadPresetFromFile(filePath);
        }

        private void UseRandomSeedCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (ChaosSeedTextBox != null && RandomizeSeedButton != null)
            {
                bool isRandom = UseRandomSeedCheck.IsChecked == true;
                ChaosSeedTextBox.IsEnabled = !isRandom;
                RandomizeSeedButton.IsEnabled = !isRandom;
            }
        }

        private void RandomizeSeedButton_Click(object sender, RoutedEventArgs e)
        {
            ChaosSeedTextBox.Text = new Random().Next().ToString();
        }

        private ChaosPreset CreatePresetFromUI(string name)
        {
            return new ChaosPreset
            {
                PresetName = name,
                IntensityOverrides = new(_intensityOverrides),
                MusicSettings = System.Text.Json.JsonSerializer.Deserialize<M64MutationSettings>(System.Text.Json.JsonSerializer.Serialize(_musicSettings))!,
                GameLogicCycleFrames = _gameLogicCycleFrames,
                Intensity = IntensifySlider.Value,
                TargetLevelIndex = LevelSelectionComboBox.SelectedIndex,
                ChaosSeed = ChaosSeedTextBox.Text,
                UseRandomSeed = UseRandomSeedCheck.IsChecked == true,

                TargetMusicNotes = TargetMusicNotesCheck.IsChecked == true,
                MusicNotesMode = _activeM64Modes.FirstOrDefault(),
                ActiveM64Modes = new List<int>(_activeM64Modes),
                M64All = M64AllRadio.IsChecked == true,
                M64Select = M64SelectRadio.IsChecked == true,
                M64KeepStructure = M64KeepStructureRadio.IsChecked == true,

                TargetSounds = TargetSoundsCheck.IsChecked == true,
                ShuffleSounds = ShuffleSoundsCheck.IsChecked == true,
                ExcludeInstrumentsShuffle = ExcludeInstrumentsShuffleCheck.IsChecked == true,
                ExcludeSfxShuffle = ExcludeSfxShuffleCheck.IsChecked == true,
                ShuffleSfxOnly = ShuffleSfxOnlyCheck.IsChecked == true,
                SfxIdentityShuffle = SfxIdentityShuffleCheck.IsChecked == true,
                SfxPitchVariation = SfxPitchVariationCheck.IsChecked == true,
                SfxRandomizerMode = GetSfxModes().FirstOrDefault(),
                ActiveSfxModes = GetSfxModes().ToList(),

                ReplaceSfx = ReplaceSfxCheck.IsChecked == true,

                RandomizeDl = RandomizeDlCheck.IsChecked == true,
                DlRandomizerMode = DlRandomizerModeComboBox.SelectedIndex,
                DlExclusion = DlExclusionCheck.IsChecked == true,

                TargetModels = TargetModelsCheck.IsChecked == true,
                TargetGoddard = TargetGoddardCheck.IsChecked == true,
                GoddardMode = GoddardModeComboBox.SelectedIndex,
                RandomizeMarioColors = RandomizeMarioColorsCheck.IsChecked == true,
                MarioColorsArea = MarioColorsAreaComboBox.SelectedIndex,
                MarioColorsMode = MarioColorsModeComboBox.SelectedIndex,

                GlitchAnimations = GlitchAnimationsCheck.IsChecked == true,
                AnimationGlitcherMode = AnimationGlitcherModeComboBox.SelectedIndex,

                GlitchHud = GlitchHudCheck.IsChecked == true,
                HudGlitcherMode = HudGlitcherModeComboBox.SelectedIndex,

                RandomizeTextures = RandomizeTexturesCheck.IsChecked == true,
                TextureRandomizeMode = TextureRandomizeModeComboBox.SelectedIndex,
                ReplaceTexturesCustom = ReplaceTexturesCustomCheck.IsChecked == true,
                ReplaceTexturesRules = _textureReplacementRules.ToList(),
                TextureRandomizeSelectedOnly = TextureRandomizeSelectedRadio.IsChecked == true,
                TextureRandomizeSelectedPaths = _selectedTextures.ToList(),

                ModeIndex = ModeComboBox.SelectedIndex,
                RandomizeSkybox = RandomizeSkyboxCheck.IsChecked == true,
                RandomizeText = RandomizeTextCheck.IsChecked == true,
                TextRandomizeMode = TextRandomizeModeComboBox.SelectedIndex,

                ChaosLogicJumpWeird = ChaosLogicJumpWeird.IsChecked == true,
                ChaosLogicJumpDeath = ChaosLogicJumpDeath.IsChecked == true,
                LimboMario = LimboMarioCheck.IsChecked == true,
                LimboMarioMode = LimboMarioModeComboBox.SelectedIndex,
                AlienSoundCheat = AudioDistortionCheck.IsChecked == true,
                AudioDistortion = AudioDistortionCheck.IsChecked == true,
                ActiveAudioDistortionModes = new List<int>(_activeAudioDistortionModes),
                GameLogicRandomizer = GameLogicRandomizerCheck.IsChecked == true,
                ActiveGameLogicModes = new List<int>(_activeGameLogicModes),
                GameLogicSimultaneous = _gameLogicSimultaneous,

                ScrambleTitleScreen = ScrambleTitleScreenCheck.IsChecked == true,
                TitleScreenScramblerMode = TitleScreenScramblerModeComboBox.SelectedIndex,
                RandomizeCutsceneCamera = RandomizeCutsceneCameraCheck.IsChecked == true,
                CutsceneCameraMode = CutsceneCameraModeComboBox.SelectedIndex,
                LakituCameraChaos = LakituCameraChaosCheck.IsChecked == true,
                LakituCameraMode = LakituCameraModeComboBox.SelectedIndex,
                StartLevelChaos = StartLevelChaosCheck.IsChecked == true,
                StartLevelIndex = StartLevelComboBox.SelectedIndex
            };
        }

        private void ApplyPresetToUI(ChaosPreset preset)
        {
            _isLoadingPreset = true;
            try
            {
                _intensityOverrides = new(preset.IntensityOverrides ?? new());
                _musicSettings = System.Text.Json.JsonSerializer.Deserialize<M64MutationSettings>(System.Text.Json.JsonSerializer.Serialize(preset.MusicSettings ?? new()))!;
                _gameLogicCycleFrames = preset.GameLogicCycleFrames;
                IntensifySlider.Value = preset.Intensity;
                if (preset.TargetLevelIndex < LevelSelectionComboBox.Items.Count)
                    LevelSelectionComboBox.SelectedIndex = preset.TargetLevelIndex;

                ChaosSeedTextBox.Text = preset.ChaosSeed;
                UseRandomSeedCheck.IsChecked = preset.UseRandomSeed;
                UseRandomSeedCheck_Toggle(null, null);

                TargetMusicNotesCheck.IsChecked = preset.TargetMusicNotes;
                _activeM64Modes.Clear();
                if (preset.ActiveM64Modes != null && preset.ActiveM64Modes.Count > 0)
                {
                    _activeM64Modes.AddRange(preset.ActiveM64Modes);
                }
                else
                {
                    _activeM64Modes.Add(preset.MusicNotesMode);
                }
                M64AllRadio.IsChecked = preset.M64All;
                M64SelectRadio.IsChecked = preset.M64Select;
                if (preset.M64KeepStructure)
                {
                    M64KeepStructureRadio.IsChecked = true;
                }
                else
                {
                    M64AlienateRadio.IsChecked = true;
                }

                TargetSoundsCheck.IsChecked = preset.TargetSounds;
                ShuffleSoundsCheck.IsChecked = preset.ShuffleSounds;
                ExcludeInstrumentsShuffleCheck.IsChecked = preset.ExcludeInstrumentsShuffle;
                ExcludeSfxShuffleCheck.IsChecked = preset.ExcludeSfxShuffle;
                ShuffleSfxOnlyCheck.IsChecked = preset.ShuffleSfxOnly;
                SfxIdentityShuffleCheck.IsChecked = preset.SfxIdentityShuffle;
                SfxPitchVariationCheck.IsChecked = preset.SfxPitchVariation;
                SetSfxModes(preset.ActiveSfxModes ?? (preset.SfxRandomizerMode == 0 ? new List<int>() : new List<int> { preset.SfxRandomizerMode }));

                ReplaceSfxCheck.IsChecked = preset.ReplaceSfx;

                RandomizeDlCheck.IsChecked = preset.RandomizeDl;
                DlRandomizerModeComboBox.SelectedIndex = preset.DlRandomizerMode;
                DlExclusionCheck.IsChecked = preset.DlExclusion;

                TargetModelsCheck.IsChecked = preset.TargetModels;
                TargetGoddardCheck.IsChecked = preset.TargetGoddard;
                GoddardModeComboBox.SelectedIndex = preset.GoddardMode;
                RandomizeMarioColorsCheck.IsChecked = preset.RandomizeMarioColors;
                MarioColorsAreaComboBox.SelectedIndex = preset.MarioColorsArea;
                MarioColorsModeComboBox.SelectedIndex = preset.MarioColorsMode;

                GlitchAnimationsCheck.IsChecked = preset.GlitchAnimations;
                AnimationGlitcherModeComboBox.SelectedIndex = preset.AnimationGlitcherMode;

                GlitchHudCheck.IsChecked = preset.GlitchHud;
                HudGlitcherModeComboBox.SelectedIndex = preset.HudGlitcherMode;

                RandomizeTexturesCheck.IsChecked = preset.RandomizeTextures;
                TextureRandomizeModeComboBox.SelectedIndex = preset.TextureRandomizeMode;
                ReplaceTexturesCustomCheck.IsChecked = preset.ReplaceTexturesCustom;
                _textureReplacementRules.Clear();
                if (preset.ReplaceTexturesRules != null)
                {
                    _textureReplacementRules.AddRange(preset.ReplaceTexturesRules);
                }
                ActiveTextureReplacementsSummaryText.Text = $"Active replacement rules: {_textureReplacementRules.Count}";
                if (preset.TextureRandomizeSelectedOnly)
                {
                    TextureRandomizeSelectedRadio.IsChecked = true;
                }
                else
                {
                    TextureRandomizeAllRadio.IsChecked = true;
                }
                _selectedTextures = preset.TextureRandomizeSelectedPaths ?? new List<string>();

                ModeComboBox.SelectedIndex = preset.ModeIndex;
                RandomizeSkyboxCheck.IsChecked = preset.RandomizeSkybox;
                RandomizeTextCheck.IsChecked = preset.RandomizeText;
                TextRandomizeModeComboBox.SelectedIndex = preset.TextRandomizeMode;

                ChaosLogicJumpWeird.IsChecked = preset.ChaosLogicJumpWeird;
                ChaosLogicJumpDeath.IsChecked = preset.ChaosLogicJumpDeath;
                LimboMarioCheck.IsChecked = preset.LimboMario;
                LimboMarioModeComboBox.SelectedIndex = preset.LimboMarioMode;
                AudioDistortionCheck.IsChecked = preset.AudioDistortion || preset.AlienSoundCheat;
                _activeAudioDistortionModes.Clear();
                if (preset.ActiveAudioDistortionModes != null)
                {
                    _activeAudioDistortionModes.AddRange(preset.ActiveAudioDistortionModes);
                }
                else
                {
                    _activeAudioDistortionModes.Add(0);
                }
                UpdateAudioDistortionSummaryText();
                GameLogicRandomizerCheck.IsChecked = preset.GameLogicRandomizer;
                _activeGameLogicModes.Clear();
                if (preset.ActiveGameLogicModes != null)
                {
                    _activeGameLogicModes.AddRange(preset.ActiveGameLogicModes);
                }
                else
                {
                    _activeGameLogicModes.AddRange(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 });
                }
                _gameLogicSimultaneous = preset.GameLogicSimultaneous;
                UpdateGameLogicModesSummaryText();

                ScrambleTitleScreenCheck.IsChecked = preset.ScrambleTitleScreen;
                TitleScreenScramblerModeComboBox.SelectedIndex = preset.TitleScreenScramblerMode;
                RandomizeCutsceneCameraCheck.IsChecked = preset.RandomizeCutsceneCamera;
                CutsceneCameraModeComboBox.SelectedIndex = preset.CutsceneCameraMode;
                LakituCameraChaosCheck.IsChecked = preset.LakituCameraChaos;
                LakituCameraModeComboBox.SelectedIndex = preset.LakituCameraMode;
                StartLevelChaosCheck.IsChecked = preset.StartLevelChaos;
                StartLevelComboBox.SelectedIndex = preset.StartLevelIndex;
            }
            finally
            {
                _isLoadingPreset = false;

                // Trigger visibility updates manually
                TargetMusicNotesCheck_Toggle(null, null);
                ShuffleSoundsCheck_Toggle(null, null);
                ReplaceSfxCheck_Toggle(null, null);
                RandomizeDlCheck_Toggle(null, null);
                DlExclusionCheck_Toggle(null, null);
                TargetGoddardCheck_Toggle(null, null);
                GlitchAnimationsCheck_Toggle(null, null);
                GlitchHudCheck_Toggle(null, null);
                RandomizeTexturesCheck_Toggle(null, null);
                ReplaceTexturesCustomCheck_Toggle(null, null);
                TextureTarget_Changed(null, null);
                RandomizeTextCheck_Toggle(null, null);
                RandomizeMarioColorsCheck_Toggle(null, null);
                M64Radio_Checked(null, null);
                ScrambleTitleScreenCheck_Toggle(null, null);
                RandomizeCutsceneCameraCheck_Toggle(null, null);
                LakituCameraChaosCheck_Toggle(null, null);
                LimboMarioCheck_Toggle(null, null);
                StartLevelChaosCheck_Toggle(null, null);
                GameLogicRandomizerCheck_Toggle(null, null);
                AudioDistortionCheck_Toggle(null, null);
            }
        }

        private void LoadPresetFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                var preset = ChaosPresetStore.Load(filePath);
                if (preset == null) return;

                ApplyPresetToUI(preset);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePresetToFile(string filePath)
        {
            var preset = CreatePresetFromUI(Path.GetFileNameWithoutExtension(filePath));

            try
            {
                ChaosPresetStore.Save(filePath, preset);
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            SavePresetToFile(Path.Combine(_presetsDir, "_default_settings.json"));
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            string name = InputBox.Show("Enter a name for the chaos preset:", "Save Chaos Preset", "MyChaosPreset");
            if (string.IsNullOrEmpty(name)) return;

            // Remove invalid file characters
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            string filePath = Path.Combine(_presetsDir, name + ".json");
            SavePresetToFile(filePath);
            LoadPresetsList(name);
            MessageBox.Show($"Preset '{name}' saved successfully!", "Preset Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetComboBox.SelectedIndex <= 0)
            {
                MessageBox.Show("Cannot delete the default settings template.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string presetName = PresetComboBox.SelectedItem.ToString();
            string filePath = Path.Combine(_presetsDir, presetName + ".json");
            if (!File.Exists(filePath)) return;

            if (MessageBox.Show($"Are you sure you want to delete preset '{presetName}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(filePath);
                    LoadPresetsList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenDlExclusionWindow_Click(object sender, RoutedEventArgs e)
        {
            var window = new DisplayListChaosWindow(_projectRoot)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        // --- QUEUE & STOCKPILE OPERATIONS ---
        private void UpdateStockpileListBox()
        {
            StockpileListBox.Items.Clear();
            foreach (var preset in _stockpile)
            {
                StockpileListBox.Items.Add(preset.PresetName);
            }
        }

        private void UpdateQueueListBox()
        {
            QueueListBox.Items.Clear();
            for (int i = 0; i < _queue.Count; i++)
            {
                QueueListBox.Items.Add($"{i + 1}. {(_queue[i].PresetName ?? "Unnamed Preset")}");
            }
        }

        private void AddCurrentToStockpile_Click(object sender, RoutedEventArgs e)
        {
            string name = InputBox.Show("Enter a name for the stockpile preset:", "Add to Stockpile", "MyStockpilePreset");
            if (string.IsNullOrEmpty(name)) return;

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            if (_stockpile.Any(p => p.PresetName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"A preset with the name '{name}' already exists in the stockpile.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var preset = CreatePresetFromUI(name);
            _stockpile.Add(preset);
            UpdateStockpileListBox();
            StatusTextBlock.Text = $"Added '{name}' to stockpile library.";
        }

        private void StockpileListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            int index = StockpileListBox.SelectedIndex;
            if (index >= 0 && index < _stockpile.Count)
            {
                ApplyPresetToUI(_stockpile[index]);
                StatusTextBlock.Text = $"Loaded '{_stockpile[index].PresetName}' settings to configuration tab.";
            }
        }

        private void QueueSelected_Click(object sender, RoutedEventArgs e)
        {
            int index = StockpileListBox.SelectedIndex;
            if (index >= 0 && index < _stockpile.Count)
            {
                var presetCopy = System.Text.Json.JsonSerializer.Deserialize<ChaosPreset>(
                    System.Text.Json.JsonSerializer.Serialize(_stockpile[index])
                );
                if (presetCopy != null)
                {
                    _queue.Add(presetCopy);
                    UpdateQueueListBox();
                    StatusTextBlock.Text = $"Queued '{presetCopy.PresetName}'.";
                }
            }
            else
            {
                MessageBox.Show("Please select a preset from the stockpile library first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RemoveStockpile_Click(object sender, RoutedEventArgs e)
        {
            int index = StockpileListBox.SelectedIndex;
            if (index >= 0 && index < _stockpile.Count)
            {
                string name = _stockpile[index].PresetName;
                _stockpile.RemoveAt(index);
                UpdateStockpileListBox();
                StatusTextBlock.Text = $"Removed '{name}' from stockpile.";
            }
        }

        private void RenameStockpile_Click(object sender, RoutedEventArgs e)
        {
            int index = StockpileListBox.SelectedIndex;
            if (index >= 0 && index < _stockpile.Count)
            {
                string oldName = _stockpile[index].PresetName;
                string newName = InputBox.Show("Enter a new name for the preset:", "Rename Stockpile Preset", oldName);
                if (string.IsNullOrEmpty(newName) || newName == oldName) return;

                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    newName = newName.Replace(c, '_');
                }

                _stockpile[index].PresetName = newName;
                UpdateStockpileListBox();
                StatusTextBlock.Text = $"Renamed '{oldName}' to '{newName}'.";
            }
        }

        private void AddCurrentToQueue_Click(object sender, RoutedEventArgs e)
        {
            string defaultName = $"Run {_queue.Count + 1}";
            string name = InputBox.Show("Enter a label for this queue item:", "Add to Queue", defaultName);
            if (string.IsNullOrEmpty(name)) return;

            var preset = CreatePresetFromUI(name);
            _queue.Add(preset);
            UpdateQueueListBox();
            StatusTextBlock.Text = $"Added '{name}' directly to active queue.";
        }

        private void MoveQueueUp_Click(object sender, RoutedEventArgs e)
        {
            int index = QueueListBox.SelectedIndex;
            if (index > 0 && index < _queue.Count)
            {
                var temp = _queue[index];
                _queue[index] = _queue[index - 1];
                _queue[index - 1] = temp;
                UpdateQueueListBox();
                QueueListBox.SelectedIndex = index - 1;
            }
        }

        private void MoveQueueDown_Click(object sender, RoutedEventArgs e)
        {
            int index = QueueListBox.SelectedIndex;
            if (index >= 0 && index < _queue.Count - 1)
            {
                var temp = _queue[index];
                _queue[index] = _queue[index + 1];
                _queue[index + 1] = temp;
                UpdateQueueListBox();
                QueueListBox.SelectedIndex = index + 1;
            }
        }

        private void RemoveQueue_Click(object sender, RoutedEventArgs e)
        {
            int index = QueueListBox.SelectedIndex;
            if (index >= 0 && index < _queue.Count)
            {
                _queue.RemoveAt(index);
                UpdateQueueListBox();
                StatusTextBlock.Text = "Removed item from queue.";
            }
        }

        private void ClearQueue_Click(object sender, RoutedEventArgs e)
        {
            _queue.Clear();
            UpdateQueueListBox();
            StatusTextBlock.Text = "Active queue cleared.";
        }

        private void SetWindowEnabled(bool enabled)
        {
            if (Content is UIElement content) content.IsEnabled = enabled;
            ChaosButton.IsEnabled = enabled;
            AddCurrentToQueueButton.IsEnabled = enabled;
            AddCurrentToStockpileButton.IsEnabled = enabled;
            RunQueueButton.IsEnabled = enabled;
            StockpileListBox.IsEnabled = enabled;
            QueueListBox.IsEnabled = enabled;
            LoadQueueButton.IsEnabled = enabled;
            SaveQueueButton.IsEnabled = enabled;
            PresetComboBox.IsEnabled = enabled;
        }

        private async void RunQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            if (_queue.Count == 0)
            {
                MessageBox.Show("Active queue is empty! Please add some presets to the queue first.", "Queue Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isBusy = true;
            SetWindowEnabled(false);
            CorruptionProgressBar.Visibility = Visibility.Visible;
            CorruptionProgressBar.Value = 0;

            _queueState.IsRunning = true;
            _queueState.IsAccumulating = AccumulateQueueCheck.IsChecked == true;
            _queueState.FilesModified.Clear();
            _queueState.Macros.Clear();
            _queueState.GameLogicModes.Clear();

            try
            {
                int totalPresets = _queue.Count;
                for (int p = 0; p < totalPresets; p++)
                {
                    var preset = _queue[p];
                    StatusTextBlock.Text = $"[Queue {p + 1}/{totalPresets}] Applying: '{preset.PresetName}'...";
                    
                    ApplyPresetToUI(preset);

                    await Task.Delay(100);

                    await InflictChaosInternalAsync();
                }

                StatusTextBlock.Text = "Queue successfully inflicted! Rebuild project to see effects.";
                MessageBox.Show("Queue successfully inflicted! Build the ROM via WSL to experience the combined glitched results.", "Queue Inflicted", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Cancelled; current entry was not applied; earlier queue entries remain";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error running Queue.";
                MessageBox.Show($"Error running Chaos Queue: {ex.Message}", "Queue Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _queueState.IsRunning = false;
                _isBusy = false;
                SetWindowEnabled(true);
                CorruptionProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadQueueFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Chaos Engine Queue Files (*.ceq)|*.ceq",
                Title = "Load Chaos Queue"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var queueFile = System.Text.Json.JsonSerializer.Deserialize<ChaosQueueFile>(json);
                    if (queueFile != null)
                    {
                        _queue.Clear();
                        if (queueFile.Queue != null)
                        {
                            _queue.AddRange(queueFile.Queue);
                        }

                        _stockpile.Clear();
                        if (queueFile.Stockpile != null)
                        {
                            _stockpile.AddRange(queueFile.Stockpile);
                        }

                        AccumulateQueueCheck.IsChecked = queueFile.Accumulate;

                        UpdateQueueListBox();
                        UpdateStockpileListBox();
                        StatusTextBlock.Text = "Queue and stockpile loaded successfully.";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load queue file: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveQueueFile_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Chaos Engine Queue Files (*.ceq)|*.ceq",
                Title = "Save Chaos Queue",
                FileName = "MyChaosQueue.ceq"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var queueFile = new ChaosQueueFile
                    {
                        Queue = _queue.ToList(),
                        Stockpile = _stockpile.ToList(),
                        Accumulate = AccumulateQueueCheck.IsChecked == true
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(queueFile, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(saveFileDialog.FileName, json);
                    StatusTextBlock.Text = "Queue and stockpile saved successfully.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save queue file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


    }

    public static class InputBox
    {
        public static string Show(string prompt, string title, string defaultValue = "")
        {
            Window window = new Window
            {
                Title = title,
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                Foreground = System.Windows.Media.Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            StackPanel sp = new StackPanel { Margin = new Thickness(15) };
            TextBlock tb = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 10), Foreground = System.Windows.Media.Brushes.White };
            TextBox txt = new TextBox { Text = defaultValue, Height = 26, Margin = new Thickness(0, 0, 0, 15), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)), Foreground = System.Windows.Media.Brushes.White, BorderBrush = System.Windows.Media.Brushes.Gray, Padding = new Thickness(3) };
            
            StackPanel btnSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button btnOk = new Button { Content = "OK", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 10, 0), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };
            Button btnCancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };

            btnSp.Children.Add(btnOk);
            btnSp.Children.Add(btnCancel);

            sp.Children.Add(tb);
            sp.Children.Add(txt);
            sp.Children.Add(btnSp);

            window.Content = sp;

            string result = "";
            btnOk.Click += (s, e) => { result = txt.Text.Trim(); window.DialogResult = true; window.Close(); };
            btnCancel.Click += (s, e) => { window.DialogResult = false; window.Close(); };

            if (window.ShowDialog() == true)
            {
                return result;
            }
            return "";
        }
    }
}

