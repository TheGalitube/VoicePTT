using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using VoicePTT.Core;
using Forms = System.Windows.Forms;

namespace VoicePTT;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly KeyboardControl _keyboard;
    private readonly PttEngine _engine;
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _trayToggle;
    private readonly List<KeyOption> _keyOptions;
    private DispatcherTimer? _bootTimer;
    private DispatcherTimer? _glitchTimer;
    private int _bootCharacterIndex;
    private bool _wasTransmitting;
    private bool _bootStarted;
    private const string BootScript = "C:\\> VOICEPTT.EXE /BOOT\n> INITIALIZING CONTROL PANEL\n> LOCAL SESSION READY_";
    private bool _ready;
    private bool _recordTarget;
    private bool _exiting;
    private bool _disposed;
    private bool _trayTipShown;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        _keyOptions = KeyCatalog.All.ToList();
        var target = KeyCatalog.FromVk(_settings.TargetVk);
        if (_keyOptions.All(option => option.Vk != target.Vk)) _keyOptions.Insert(0, target);
        TargetKeyCombo.ItemsSource = _keyOptions;
        TargetKeyCombo.SelectedItem = _keyOptions.First(option => option.Vk == target.Vk);
        ModeCombo.SelectedIndex = _settings.Mode switch { "Tap-to-talk" => 1, "Hold-to-talk" => 2, _ => 0 };
        ThresholdSlider.Value = _settings.ThresholdDb;
        ReleaseSlider.Value = _settings.ReleaseThresholdDb;
        DelaySlider.Value = _settings.ReleaseDelayMs;
        TapWindowSlider.Value = _settings.TapWindowMs;
        LaunchCheck.IsChecked = _settings.LaunchAtSignIn;
        TrayCheck.IsChecked = _settings.CloseToTray;
        RefreshDevices();

        _keyboard = new KeyboardControl(Dispatcher);
        if (!HotkeySpec.TryParse(_settings.Hotkey, out var shortcut))
            shortcut = new HotkeySpec(0x56, true, true, false, false);
        _settings.Hotkey = shortcut.Display;
        _keyboard.SetHotkey(shortcut);
        _keyboard.HotkeyRecorded += OnRecorded;

        _engine = new PttEngine(_keyboard, Dispatcher, _settings);
        _keyboard.HotkeyPressed += _engine.OnHotkeyPressed;
        _keyboard.HotkeyReleased += _engine.OnHotkeyReleased;
        _engine.StateChanged += RefreshState;
        _engine.Fault += ShowFault;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
        _meterTimer.Tick += (_, _) => { _engine.Tick(); UpdateMeter(); };
        _meterTimer.Start();

        _trayToggle = new Forms.ToolStripMenuItem("Enable VoicePTT", null, (_, _) => Dispatcher.BeginInvoke(() => _engine.Toggle()));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Open VoicePTT", null, (_, _) => Dispatcher.BeginInvoke(ShowWindow)));
        menu.Items.Add(_trayToggle);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => Dispatcher.BeginInvoke(ExitApp)));
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? System.Drawing.SystemIcons.Application,
            Text = "VoicePTT — Offline",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowWindow);

        _ready = true;
        UpdateModeDetails();
        RefreshState();
        StatusMessage.Text = "Ready. Set the PTT key in your game to match the key above.";
    }

    public void ShowWindow()
    {
        var wasHidden = !IsVisible;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        if (wasHidden && BootOverlay.Visibility != Visibility.Visible) AnimatePanelEntrance(8, 300);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_bootStarted) return;
        _bootStarted = true;
        AlertChip.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(800))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        BootConsole.Text = "";
        _bootTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(24) };
        _bootTimer.Tick += (_, _) =>
        {
            if (_bootCharacterIndex < BootScript.Length)
            {
                BootConsole.Text += BootScript[_bootCharacterIndex++];
                BootProgress.Value = 100.0 * _bootCharacterIndex / BootScript.Length;
                return;
            }
            _bootTimer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(430))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => BootOverlay.Visibility = Visibility.Collapsed;
            BootOverlay.BeginAnimation(OpacityProperty, fade);
            AnimatePanelEntrance(18, 500);
            _glitchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            _glitchTimer.Tick += (_, _) =>
            {
                GlitchTitle.Text = "VO!CEPTT.EXE";
                GlitchTitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 105, 124));
                var reset = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(85) };
                reset.Tick += (_, _) =>
                {
                    reset.Stop();
                    GlitchTitle.Text = "VOICEPTT.EXE";
                    GlitchTitle.Foreground = (System.Windows.Media.Brush)FindResource("Mint");
                };
                reset.Start();
            };
            _glitchTimer.Start();
        };
        _bootTimer.Start();
    }

    private void AnimatePanelEntrance(double fromY, int durationMs)
    {
        MainScroll.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        MainRise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void RefreshDevices()
    {
        try
        {
            var devices = MicrophoneMonitor.ListDevices();
            DeviceCombo.ItemsSource = devices;
            DeviceCombo.SelectedItem = devices.FirstOrDefault(d => d.Id == _settings.DeviceId)
                ?? devices.FirstOrDefault(d => d.Name == _settings.LegacyDeviceName)
                ?? devices[0];
            _settings.DeviceId = ((DeviceOption)DeviceCombo.SelectedItem).Id;
            _settings.LegacyDeviceName = "";
        }
        catch (Exception ex)
        {
            DeviceCombo.ItemsSource = new[] { new DeviceOption("", "No microphone found") };
            DeviceCombo.SelectedIndex = 0;
            StatusMessage.Text = "Could not list microphones: " + ex.Message;
        }
    }

    private void UpdateSettingsFromUi()
    {
        if (!_ready) return;
        _settings.Mode = ((ComboBoxItem?)ModeCombo.SelectedItem)?.Content?.ToString() ?? "Voice activity";
        _settings.DeviceId = (DeviceCombo.SelectedItem as DeviceOption)?.Id ?? "";
        _settings.TargetVk = (TargetKeyCombo.SelectedItem as KeyOption)?.Vk ?? _settings.TargetVk;
        _settings.ThresholdDb = Math.Round(ThresholdSlider.Value);
        _settings.ReleaseThresholdDb = Math.Min(Math.Round(ReleaseSlider.Value), _settings.ThresholdDb);
        _settings.ReleaseDelayMs = (int)Math.Round(DelaySlider.Value / 50) * 50;
        _settings.TapWindowMs = (int)Math.Round(TapWindowSlider.Value / 100) * 100;
        _settings.LaunchAtSignIn = LaunchCheck.IsChecked == true;
        _settings.CloseToTray = TrayCheck.IsChecked == true;
        _engine.UpdateSettings(_settings);
        UpdateLabels();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void UpdateLabels()
    {
        ThresholdValue.Text = $"{_settings.ThresholdDb:0} dB";
        ReleaseValue.Text = $"{_settings.ReleaseThresholdDb:0} dB";
        DelayValue.Text = $"{_settings.ReleaseDelayMs} ms";
        TapWindowValue.Text = $"{_settings.TapWindowMs} ms";
        HotkeyText.Text = _settings.Hotkey;
    }

    private void UpdateModeDetails()
    {
        ModeDescription.Text = _settings.Mode switch
        {
            "Tap-to-talk" => "Tap the hotkey to open PTT. Speech keeps it open; silence closes it.",
            "Hold-to-talk" => "Hold the hotkey while you speak. Release it to close PTT.",
            _ => "Speech opens PTT automatically. Silence closes it after the chosen delay."
        };
        HotkeyDescription.Text = _settings.Mode switch
        {
            "Tap-to-talk" => "Tap this key to begin a transmission. Tap again to cancel it.",
            "Hold-to-talk" => "Hold this key to transmit. It is intercepted before your game sees it.",
            _ => "Press this key to enable or disable voice detection."
        };
        var hasMicrophone = _settings.Mode != "Hold-to-talk";
        ThresholdSlider.IsEnabled = hasMicrophone;
        ReleaseSlider.IsEnabled = hasMicrophone;
        DelaySlider.IsEnabled = hasMicrophone;
        TapWindowRow.Visibility = _settings.Mode == "Tap-to-talk" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshState()
    {
        StateText.Text = _engine.StateLabel;
        DetailText.Text = _engine.DetailText;
        StateText.Foreground = _engine.IsTransmitting ? (System.Windows.Media.Brush)FindResource("Mint") : (System.Windows.Media.Brush)FindResource("Ink");
        if (_wasTransmitting != _engine.IsTransmitting)
        {
            _wasTransmitting = _engine.IsTransmitting;
            StateText.BeginAnimation(OpacityProperty, _wasTransmitting
                ? new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(550)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                : null);
            if (!_wasTransmitting) StateText.Opacity = 1;
        }
        EnableButton.Content = _engine.IsActive ? "Disable VoicePTT" : "Enable VoicePTT";
        _trayToggle.Text = _engine.IsActive ? "Disable VoicePTT" : "Enable VoicePTT";
        _tray.Text = "VoicePTT — " + (_engine.IsTransmitting ? "Transmitting" : _engine.IsActive ? "Listening" : "Offline");
        ModeCombo.IsEnabled = !_engine.IsActive;
        DeviceCombo.IsEnabled = !_engine.IsActive;
        TargetKeyCombo.IsEnabled = !_engine.IsActive;
        RecordTargetButton.IsEnabled = !_engine.IsActive;
        RecordHotkeyButton.IsEnabled = !_engine.IsActive;
    }

    private void UpdateMeter()
    {
        var db = _engine.LevelDb;
        LevelMeter.Value = Math.Clamp((db + 60) / 60 * 100, 0, 100);
        LevelText.Text = _engine.IsActive && _settings.Mode != "Hold-to-talk" ? $"{db:0} dB" : "— dB";
    }

    private void ShowFault(string message)
    {
        StatusMessage.Text = message;
        if (!IsVisible) _tray.ShowBalloonTip(4000, "VoicePTT needs attention", message, Forms.ToolTipIcon.Warning);
        RefreshState();
    }

    private void SaveNow()
    {
        try { SettingsStore.Save(_settings); }
        catch (Exception ex) { StatusMessage.Text = "Could not save settings: " + ex.Message; }
    }

    private void BeginRecording(bool target)
    {
        if (_engine.IsActive) { StatusMessage.Text = "Disable VoicePTT before changing keys."; return; }
        _recordTarget = target;
        _keyboard.BeginRecording();
        StatusMessage.Text = target ? "Press the PTT key to record it. Escape cancels." :
            "Press your hotkey, including any modifiers. Escape cancels.";
        if (target) RecordTargetButton.Content = "Listening…";
        else RecordHotkeyButton.Content = "Listening…";
    }

    private void OnRecorded(HotkeySpec? recorded)
    {
        RecordTargetButton.Content = "Record";
        RecordHotkeyButton.Content = "Record";
        if (recorded is null) { StatusMessage.Text = "Key recording cancelled."; return; }
        if (_recordTarget)
        {
            var key = KeyCatalog.FromVk(recorded.Vk);
            if (_keyOptions.All(option => option.Vk != key.Vk)) _keyOptions.Insert(0, key);
            TargetKeyCombo.ItemsSource = null;
            TargetKeyCombo.ItemsSource = _keyOptions;
            TargetKeyCombo.SelectedItem = _keyOptions.First(option => option.Vk == key.Vk);
            StatusMessage.Text = $"PTT key set to {key.Name}. Set the same key in your game or app.";
        }
        else
        {
            _settings.Hotkey = recorded.Display;
            _keyboard.SetHotkey(recorded);
            HotkeyText.Text = recorded.Display;
            StatusMessage.Text = $"Global hotkey set to {recorded.Display}.";
        }
        UpdateSettingsFromUi();
    }

    private void EnableButton_Click(object sender, RoutedEventArgs e) => _engine.Toggle();
    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsActive) { StatusMessage.Text = "Disable VoicePTT before refreshing microphones."; return; }
        RefreshDevices();
        UpdateSettingsFromUi();
    }
    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateSettingsFromUi(); if (_ready) UpdateModeDetails(); }
    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSettingsFromUi();
    private void TargetKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSettingsFromUi();
    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready && ReleaseSlider.Value > ThresholdSlider.Value) ReleaseSlider.Value = ThresholdSlider.Value;
        UpdateSettingsFromUi();
    }
    private void ReleaseSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready && ReleaseSlider.Value > ThresholdSlider.Value) ReleaseSlider.Value = ThresholdSlider.Value;
        UpdateSettingsFromUi();
    }
    private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSettingsFromUi();
    private void TapWindowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSettingsFromUi();
    private void RecordTargetButton_Click(object sender, RoutedEventArgs e) => BeginRecording(true);
    private void RecordHotkeyButton_Click(object sender, RoutedEventArgs e) => BeginRecording(false);
    private void ScrollLockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsActive) { StatusMessage.Text = "Disable VoicePTT before changing the hotkey."; return; }
        var shortcut = new HotkeySpec(0x91, false, false, false, false);
        _settings.Hotkey = shortcut.Display;
        _keyboard.SetHotkey(shortcut);
        UpdateSettingsFromUi();
        StatusMessage.Text = "Scroll Lock is now the global control hotkey.";
    }
    private void LaunchCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (LaunchCheck.IsChecked == true) run?.SetValue("VoicePTT", $"\"{Environment.ProcessPath}\"");
            else run?.DeleteValue("VoicePTT", false);
            UpdateSettingsFromUi();
        }
        catch (Exception ex) { StatusMessage.Text = "Could not change Windows startup: " + ex.Message; LaunchCheck.IsChecked = false; }
    }
    private void TrayCheck_Changed(object sender, RoutedEventArgs e) => UpdateSettingsFromUi();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayTipShown)
            {
                _tray.ShowBalloonTip(3000, "VoicePTT is in the tray", "Open or exit it from the tray icon.", Forms.ToolTipIcon.Info);
                _trayTipShown = true;
            }
            return;
        }
        ShutdownResources();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void ShutdownResources()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Stop();
        _bootTimer?.Stop();
        _glitchTimer?.Stop();
        SaveNow();
        _meterTimer.Stop();
        _engine.Dispose();
        _keyboard.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
