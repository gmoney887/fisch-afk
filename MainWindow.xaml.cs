using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FischMacroCS.Core;
using FischMacroCS.Native;
using OpenCvSharp.WpfExtensions;
using Mat = OpenCvSharp.Mat;

namespace FischMacroCS;

public partial class MainWindow : Window
{
    private const int HOTKEY_ID_TOGGLE = 9001;
    private const int HOTKEY_ID_REEQUIP = 9002;
    private const int HOTKEY_ID_END = 9003;

    private readonly Settings _settings;
    private readonly FishingEngine _engine;
    private readonly GlobalKeyboardHook _keyboardHook;

    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource = null;
    private int _isTelemetryPending = 0;
    private WriteableBitmap? _previewBitmap = null;

    public MainWindow()
    {
        string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_startup.log");
        try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] MainWindow constructor start\n"); } catch { }

        try
        {
            _settings = Settings.Load();
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Settings.Load OK\n");

            InitializeComponent();
            ApplyAppVersion();
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] InitializeComponent OK\n");

            _engine = new FishingEngine(_settings);
            _engine.OnTelemetry += Engine_OnTelemetry;
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] FishingEngine OK\n");

            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.OnToggle += () => Dispatcher.BeginInvoke(ToggleMacro);
            _keyboardHook.OnReEquip += () => _engine.ReEquipRod();
            _keyboardHook.OnStop += () => Dispatcher.BeginInvoke(() => { if (_engine.IsRunning) ToggleMacro(); });
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] GlobalKeyboardHook OK\n");

            PopulateSettingsUI();
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] PopulateSettingsUI OK\n");

            Loaded += MainWindow_Loaded;
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] MainWindow constructor complete\n");
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] EXCEPTION IN CONSTRUCTOR: {ex}\n"); } catch { }
            throw;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_startup.log");
        try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] OnSourceInitialized HWND=0x{_hwnd:X}\n"); } catch { }

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHook);

        RegisterHotkeys();
        try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] RegisterHotkeys OK\n"); } catch { }
    }

    private void RegisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Unregister any previous hotkeys
        Win32.UnregisterHotKey(_hwnd, HOTKEY_ID_TOGGLE);
        Win32.UnregisterHotKey(_hwnd, HOTKEY_ID_REEQUIP);
        Win32.UnregisterHotKey(_hwnd, HOTKEY_ID_END);

        uint toggleVk = Win32.ParseVirtualKey(_settings.ToggleHotkey);
        uint reequipVk = Win32.ParseVirtualKey(_settings.ReEquipHotkey);

        // Update low-level keyboard hook targets (immune to Error 1409 conflicts)
        if (_keyboardHook != null)
        {
            _keyboardHook.ToggleVk = toggleVk;
            _keyboardHook.ReEquipVk = reequipVk;
            _keyboardHook.StopVk = (uint)Win32.VK_END;
        }

        // Secondary fallback to Win32 RegisterHotKey
        Win32.RegisterHotKey(_hwnd, HOTKEY_ID_TOGGLE, Win32.MOD_NONE, toggleVk);
        Win32.RegisterHotKey(_hwnd, HOTKEY_ID_REEQUIP, Win32.MOD_NONE, reequipVk);
        Win32.RegisterHotKey(_hwnd, HOTKEY_ID_END, Win32.MOD_NONE, (uint)Win32.VK_END);

        // Update UI labels
        TxtHeaderHotkey.Text = $"[{_settings.ToggleHotkey.ToUpperInvariant()}] START/STOP";
        if (BtnToggle.Template?.FindName("txtBtnSub", BtnToggle) is TextBlock subTxt)
            subTxt.Text = $"Press {_settings.ToggleHotkey.ToUpperInvariant()}";
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID_TOGGLE)
            {
                ToggleMacro();
                handled = true;
            }
            else if (id == HOTKEY_ID_REEQUIP)
            {
                _engine.ReEquipRod();
                handled = true;
            }
            else if (id == HOTKEY_ID_END)
            {
                if (_engine.IsRunning)
                    ToggleMacro();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void PopulateSettingsUI()
    {
        TxtRodSlot.Text = _settings.RodSlot;
        TxtPostCatch.Text = _settings.PostCatchDelayMs.ToString();
        TxtCastLead.Text = _settings.CastPredictiveLeadMs.ToString();

        SelectComboItem(CmbToggleKey, _settings.ToggleHotkey);
        SelectComboItem(CmbReEquipKey, _settings.ReEquipHotkey);
        ChkEnableRecording.IsChecked = _settings.EnableRecording;
        TxtMaxRecordings.Text = _settings.MaxRecordingsToKeep.ToString();
        if (!string.IsNullOrEmpty(_settings.ShakeMode))
        {
            if (_settings.ShakeMode.Equals("Navigation", StringComparison.OrdinalIgnoreCase))
                SelectComboItem(CmbShakeMode, "UI Navigation (Key Shake)");
            else if (_settings.ShakeMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
                SelectComboItem(CmbShakeMode, "Disabled");
            else
                SelectComboItem(CmbShakeMode, "Visual (Auto-Click Icon)");
        }
        else
        {
            SelectComboItem(CmbShakeMode, _settings.EnableShakeClicks ? "Visual (Auto-Click Icon)" : "Disabled");
        }

        // Anti-AFK & Jitter
        ChkAntiAfk.IsChecked = _settings.EnableAntiAfk;
        ChkJitter.IsChecked = _settings.EnableHumanizedJitter;

        // Aquarium Auto-Claim
        ChkAutoClaimAquarium.IsChecked = _settings.EnableAutoClaimAquarium;
        TxtAquariumInterval.Text = _settings.AquariumClaimIntervalMinutes.ToString();
        CmbMinigameTheme.SelectedIndex = (int)_settings.SelectedTheme;

        // Auto-Open Crates ('g' Inventory)
        ChkAutoOpenCrates.IsChecked = _settings.EnableAutoOpenCrates;
        ChkAutoPreFlight.IsChecked = _settings.AutoRunPreFlightOnStart;
        TxtCrateInterval.Text = _settings.CrateIntervalCatches.ToString();
        TxtCrateMaxTypes.Text = _settings.CrateMaxTypes.ToString();

        // Session Analytics Initial State
        TxtTotalCatches.Text = _engine.TotalCatches.ToString();
        TxtCatchRate.Text = $"{_engine.CatchesPerHour:F1}/hr";
        TxtWinRate.Text = $"{_engine.WinRate:F0}%";
        TxtSessionUptime.Text = TimeSpan.FromSeconds(_engine.SessionUptimeSeconds).ToString(@"hh\:mm\:ss");
        TxtStreakBadge.Text = $"🔥 Streak: {_engine.CurrentStreak}";

        ChkAlwaysOnTop.IsChecked = _settings.AlwaysOnTop;
        this.Topmost = _settings.AlwaysOnTop;
    }

    private void ApplyAppVersion()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var infoVerAttr = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            string rawVer = infoVerAttr?.InformationalVersion?.Split('+')[0] 
                            ?? asm.GetName().Version?.ToString(3) 
                            ?? "1.0.0";
            if (!rawVer.StartsWith("v", StringComparison.OrdinalIgnoreCase)) rawVer = "v" + rawVer;

            Title = $"Fat Dad's Fisch AFK Pro {rawVer}";
            if (TxtAppVersionBadge != null) TxtAppVersionBadge.Text = $"{rawVer} PRO";
            if (TxtSplashVersion != null) TxtSplashVersion.Text = $"AUTONOMOUS KINETIC ANGLER • ROBLOX FISCH • {rawVer}";
            if (TxtFooterVersion != null) TxtFooterVersion.Text = $"Fat Dad's Fisch AFK Pro {rawVer}";
        }
        catch { }
    }

    private void SelectComboItem(ComboBox combo, string target)
    {
        if (string.IsNullOrEmpty(target)) return;
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Content != null)
            {
                string text = cbi.Content.ToString()!;
                if (string.Equals(text, target, StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith(target, StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
        }
    }

    private bool _isStopping = false;

    private void ToggleMacro()
    {
        if (_isStopping || _isDiagnosticRunning) return; // Prevent double-clicks while shutting down or during pre-flight

        if (_engine.IsRunning)
        {
            // Graceful Queued Stop: If reeling and not already queued, wait until catch finishes
            if (_engine.CurrentState == MacroState.Reeling && !_engine.IsStopQueued)
            {
                _engine.IsStopQueued = true;
                Dispatcher.Invoke(() =>
                {
                    if (BtnToggle.Template.FindName("btnBorder", BtnToggle) is Border border)
                        border.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                    if (BtnToggle.Template.FindName("txtBtnState", BtnToggle) is TextBlock txt)
                        txt.Text = "QUEUED STOP";
                    if (BtnToggle.Template.FindName("txtBtnSub", BtnToggle) is TextBlock subTxt)
                        subTxt.Text = "Stopping after catch (F6 force)";
                });
                return;
            }

            _isStopping = true;
            _engine.IsStopQueued = false;
            
            // Instantly transition UI to "STOPPING..." state
            Dispatcher.Invoke(() =>
            {
                if (BtnToggle.Template.FindName("btnBorder", BtnToggle) is Border border)
                    border.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                if (BtnToggle.Template.FindName("txtBtnState", BtnToggle) is TextBlock txt)
                    txt.Text = "STOPPING...";
                if (BtnToggle.Template.FindName("txtBtnSub", BtnToggle) is TextBlock subTxt)
                    subTxt.Text = "Please wait";
            });

            // Queue actual shutdown asynchronously so UI thread doesn't hang
            System.Threading.Tasks.Task.Run(() =>
            {
                _engine.Stop();
                _isStopping = false;
                Dispatcher.Invoke(() => UpdateUIState(false));
            });
        }
        else
        {
            if (_settings.AutoRunPreFlightOnStart)
            {
                _ = Task.Run(async () =>
                {
                    bool passed = false;
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        passed = await RunPreFlightDiagnosticAsync();
                    });

                    if (passed)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _engine.Start();
                            UpdateUIState(true);
                        });
                    }
                });
                return;
            }

            _engine.Start();
            UpdateUIState(true);
        }
    }

    private void UpdateUIState(bool running)
    {
        Dispatcher.Invoke(() =>
        {
            if (running)
            {
                if (BtnToggle.Template.FindName("btnBorder", BtnToggle) is Border border)
                    border.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Friendly coral/red (#EF4444)
                if (BtnToggle.Template.FindName("txtBtnState", BtnToggle) is TextBlock txt)
                    txt.Text = "STOP FISHING";
                if (BtnToggle.Template.FindName("txtBtnSub", BtnToggle) is TextBlock subTxt)
                    subTxt.Text = $"Press {_settings.ToggleHotkey.ToUpperInvariant()}";

                if (TxtTasksIdleBadge != null)
                {
                    TxtTasksIdleBadge.Text = "(Busy Fishing)";
                    TxtTasksIdleBadge.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }

                SetIdleButtonsEnabled(false);
            }
            else
            {
                if (BtnToggle.Template.FindName("btnBorder", BtnToggle) is Border border)
                    border.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Friendly emerald green (#10B981)
                if (BtnToggle.Template.FindName("txtBtnState", BtnToggle) is TextBlock txt)
                    txt.Text = "START FISHING";
                if (BtnToggle.Template.FindName("txtBtnSub", BtnToggle) is TextBlock subTxt)
                    subTxt.Text = $"Press {_settings.ToggleHotkey.ToUpperInvariant()}";

                if (TxtTasksIdleBadge != null)
                {
                    TxtTasksIdleBadge.Text = "(Idle Only)";
                    TxtTasksIdleBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                }

                SetIdleButtonsEnabled(true);

                TxtState.Text = "READY";
                BadgeState.Background = new SolidColorBrush(Color.FromRgb(55, 65, 81));
                TxtAction.Text = "Idle";
                TxtBarPos.Text = "Waiting...";
                TxtFishPos.Text = "Waiting...";
                TxtLatency.Text = "Vision: -- | Loop: --";
            }
        });
    }

    private void SetIdleButtonsEnabled(bool enabled)
    {
        string tip = enabled 
            ? "" 
            : "Cannot run while actively fishing. Stop fishing first.";

        if (BtnClaimAquarium != null)
        {
            BtnClaimAquarium.IsEnabled = enabled;
            BtnClaimAquarium.Opacity = enabled ? 1.0 : 0.45;
            BtnClaimAquarium.ToolTip = enabled ? "Requires NOT Fishing: Opens Aquarium, claims hourly profit (C$ + XP), and returns" : tip;
        }
        if (BtnOpenCrates != null)
        {
            BtnOpenCrates.IsEnabled = enabled;
            BtnOpenCrates.Opacity = enabled ? 1.0 : 0.45;
            BtnOpenCrates.ToolTip = enabled ? "Requires NOT Fishing: Unequips rod, opens Equipment ('g'), searches 'crate', unpacks all crates, and re-equips rod" : tip;
        }
        if (BtnTestClaim != null)
        {
            BtnTestClaim.IsEnabled = enabled;
            BtnTestClaim.Opacity = enabled ? 1.0 : 0.45;
            BtnTestClaim.ToolTip = enabled ? "Requires NOT Fishing: Test Aquarium claim sequence immediately" : tip;
        }
        if (BtnTestOpenCrates != null)
        {
            BtnTestOpenCrates.IsEnabled = enabled;
            BtnTestOpenCrates.Opacity = enabled ? 1.0 : 0.45;
            BtnTestOpenCrates.ToolTip = enabled ? "Requires NOT Fishing: Test Crate unpack sequence immediately" : tip;
        }
    }

    private void Engine_OnTelemetry(TelemetryData t)
    {
        // Drop intermediate frame immediately if UI dispatcher is already processing a frame
        if (System.Threading.Interlocked.CompareExchange(ref _isTelemetryPending, 1, 0) != 0)
        {
            t.Dispose();
            return;
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            try
            {
                // Update State Badge
                TxtState.Text = t.State.ToString().ToUpper();
                if (t.State == MacroState.Stopped && !_isStopping)
                {
                    UpdateUIState(false);
                }
                BadgeState.Background = t.State switch
                {
                    MacroState.Reeling => new SolidColorBrush(Color.FromRgb(16, 185, 129)), // Emerald
                    MacroState.Luring => new SolidColorBrush(Color.FromRgb(245, 158, 11)),  // Amber
                    MacroState.Casting => new SolidColorBrush(Color.FromRgb(59, 130, 246)), // Blue
                    MacroState.PostCatch => new SolidColorBrush(Color.FromRgb(139, 92, 246)), // Purple
                    _ => new SolidColorBrush(Color.FromRgb(55, 65, 81))
                };

                // Update Rod Vision Status Badge
                bool isRodEquipped = t.IsRodEquipped;
                TxtRodState.Text = isRodEquipped ? "ROD: EQUIPPED" : "ROD: UNEQUIPPED";
                TxtRodState.Foreground = isRodEquipped ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(248, 113, 113));
                BadgeRodState.Background = isRodEquipped ? new SolidColorBrush(Color.FromRgb(12, 46, 36)) : new SolidColorBrush(Color.FromRgb(69, 26, 26));
                BadgeRodState.BorderBrush = isRodEquipped ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));

                if (TxtMonitorRod != null && MonitorRodBadge != null)
                {
                    TxtMonitorRod.Text = isRodEquipped ? "ROD: EQUIPPED" : "ROD: UNEQUIPPED";
                    TxtMonitorRod.Foreground = isRodEquipped ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    MonitorRodBadge.Background = isRodEquipped ? new SolidColorBrush(Color.FromRgb(22, 43, 32)) : new SolidColorBrush(Color.FromRgb(55, 20, 20));
                    MonitorRodBadge.BorderBrush = isRodEquipped ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }

                // Update Action
                TxtAction.Text = string.IsNullOrEmpty(t.Action) ? "Tracking..." : t.Action;

                // Update Bar & Fish Stats
                if (t.BarWidth > 0)
                    TxtBarPos.Text = $"[{t.BarLeft}..{t.BarRight}] ({t.BarCenter:F0})";
                else
                    TxtBarPos.Text = "Not Detected";

                if (t.FishX > 0)
                    TxtFishPos.Text = $"{t.FishX}px (Err: {t.Error:+0;-0;0}px)";
                else
                    TxtFishPos.Text = "Searching...";

                // Update Latency & Rod Acceleration
                TxtLatency.Text = $"Vision: {t.VisionLatencyMs:F1}ms | Rod: {t.RodPullAccel:F0}px/s²";

                // Update Session Analytics
                TxtTotalCatches.Text = _engine.TotalCatches.ToString();
                TxtCatchRate.Text = $"{_engine.CatchesPerHour:F1}/hr";
                TxtWinRate.Text = $"{_engine.WinRate:F0}%";
                TxtSessionUptime.Text = TimeSpan.FromSeconds(_engine.SessionUptimeSeconds).ToString(@"hh\:mm\:ss");
                TxtStreakBadge.Text = $"🔥 Streak: {_engine.CurrentStreak}";

                // Update Recording Indicator
                TxtRecordingIndicator.Visibility = _engine.Recorder.IsRecording ? Visibility.Visible : Visibility.Collapsed;

                // Update Live Action Overlay on Camera Preview
                if (t.State == MacroState.Reeling && !string.IsNullOrEmpty(t.Action))
                {
                    PnlActionOverlay.Visibility = Visibility.Visible;
                    TxtOverlayAction.Text = t.Action.ToUpper();
                    BadgeOverlayAction.Background = t.Action switch
                    {
                        var a when a.Contains("EMERGENCY") => new SolidColorBrush(Color.FromRgb(220, 38, 38)), // Crimson
                        var a when a.Contains("RIGHT")     => new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red
                        var a when a.Contains("LEFT")      => new SolidColorBrush(Color.FromRgb(59, 130, 246)),// Blue
                        var a when a.Contains("Locked")    => new SolidColorBrush(Color.FromRgb(16, 185, 129)),// Emerald
                        var a when a.Contains("Coast")     => new SolidColorBrush(Color.FromRgb(168, 85, 247)),// Purple
                        var a when a.Contains("Brake")     => new SolidColorBrush(Color.FromRgb(249, 115, 22)), // Orange
                        _ => new SolidColorBrush(Color.FromRgb(6, 182, 212))                                    // Cyan Tracking
                    };

                    TxtOverlayStats.Text = $"Fish: {t.FishX}px | Center: {t.BarCenter:F0}px | Err: {t.Error:+0;-0;0}px | Rod: {t.RodPullAccel:F0}px/s²";

                    bool isMouseDown = t.IsMouseDown;
                    TxtOverlayMouse.Text = isMouseDown ? "● CLICK DOWN" : "○ RELEASED";
                    BadgeOverlayMouse.Background = isMouseDown 
                        ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) 
                        : new SolidColorBrush(Color.FromRgb(31, 41, 55));
                    TxtOverlayMouse.Foreground = isMouseDown 
                        ? Brushes.White 
                        : new SolidColorBrush(Color.FromRgb(156, 163, 175));
                }
                else
                {
                    PnlActionOverlay.Visibility = Visibility.Collapsed;
                }

                // Update Live Camera Preview with ZERO heap allocation (reusing single D3D WriteableBitmap backbuffer)
                if (ChkShowPreview.IsChecked == true && t.AnnotatedFrame != null && !t.AnnotatedFrame.Empty())
                {
                    RenderPreviewFrame(t.AnnotatedFrame);
                }
            }
            finally
            {
                t.Dispose();
                System.Threading.Interlocked.Exchange(ref _isTelemetryPending, 0);
            }
        }));
    }

    private void BtnToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleMacro();
    }

    private void BtnReEquip_Click(object sender, RoutedEventArgs e)
    {
        _engine.ReEquipRod();
    }

    private void BtnOpenRecordings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = _engine.Recorder.RecordingsDirectory;
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Unable to open recordings folder: " + ex.Message);
        }
    }

    private void BtnSnapSideBySide_Click(object sender, RoutedEventArgs e)
    {
        IntPtr robloxHwnd = Win32.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero)
        {
            MessageBox.Show("Roblox window was not detected. Please ensure Roblox is running and in windowed mode (press F11 in Roblox to exit full screen if needed).", "Fat Dad's Fisch AFK Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Expand settings expander in side-by-side mode where there is plenty of vertical room
        if (ExpanderSettings != null)
        {
            ExpanderSettings.IsExpanded = true;
        }

        IntPtr macroHwnd = new WindowInteropHelper(this).Handle;
        Win32.SnapWindowsSideBySide(robloxHwnd, macroHwnd);
        ChkAlwaysOnTop.IsChecked = true;
        this.Topmost = true;
    }

    private void BtnWindowed_Click(object sender, RoutedEventArgs e)
    {
        IntPtr robloxHwnd = Win32.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero)
        {
            MessageBox.Show("Roblox window was not detected. Please ensure Roblox is running.", "Fat Dad's Fisch AFK Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Collapse configuration drawer so the floating window is compact with zero scroll
        if (ExpanderSettings != null)
        {
            ExpanderSettings.IsExpanded = false;
        }

        // Calculate DPI-aware pixel dimensions for comfortable readable windowed mode (465 x 585 DIPs)
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        int targetPixelW = (int)Math.Round(465 * dpiX);
        int targetPixelH = (int)Math.Round(585 * dpiY);

        this.Width = 465;
        this.Height = 585;

        IntPtr macroHwnd = new WindowInteropHelper(this).Handle;
        Win32.DockFullScreenAndTopRight(robloxHwnd, macroHwnd, targetPixelW, targetPixelH);
        ChkAlwaysOnTop.IsChecked = true;
        this.Topmost = true;
    }

    private void ExpanderSettings_Expanded(object sender, RoutedEventArgs e)
    {
        // If window is currently in compact mode, expand height smoothly so settings fit without scrolling
        if (this.Height < 700)
        {
            var source = PresentationSource.FromVisual(this);
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            if (Win32.SystemParametersInfo(Win32.SPI_GETWORKAREA, 0, out Win32.RECT rcWork, 0))
            {
                int maxH = (int)(rcWork.Height / dpiY * 0.92);
                this.Height = Math.Min(910, maxH);
            }
            else
            {
                this.Height = 880;
            }
        }
    }

    private void ExpanderSettings_Collapsed(object sender, RoutedEventArgs e)
    {
        // When settings drawer is closed, return to sleek compact dashboard height
        if (this.Height > 620 && this.Height < 1000)
        {
            this.Height = 585;
        }
    }

    private void BtnFullScreenTopRight_Click(object sender, RoutedEventArgs e) => BtnWindowed_Click(sender, e);

    private void ChkAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
    {
        bool onTop = ChkAlwaysOnTop.IsChecked == true;
        this.Topmost = onTop;
        if (_settings != null)
        {
            _settings.AlwaysOnTop = onTop;
            _settings.Save();
        }
    }


    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.RodSlot = TxtRodSlot.Text.Trim();
        if (int.TryParse(TxtPostCatch.Text, out int pc))
        {
            _settings.PostCatchDelayMs = pc;
            _engine.Config.PostCatchDelayMs = pc;
        }

        if (int.TryParse(TxtCastLead.Text, out int cl))
        {
            _settings.CastPredictiveLeadMs = cl;
            _engine.Config.CastPredictiveLeadMs = cl;
        }

        if (CmbToggleKey.SelectedItem is ComboBoxItem toggleItem && toggleItem.Content != null)
            _settings.ToggleHotkey = toggleItem.Content.ToString()!;

        if (CmbReEquipKey.SelectedItem is ComboBoxItem reequipItem && reequipItem.Content != null)
            _settings.ReEquipHotkey = reequipItem.Content.ToString()!;

        _settings.EnableRecording = ChkEnableRecording.IsChecked == true;
        if (CmbShakeMode.SelectedItem is ComboBoxItem shakeItem && shakeItem.Content != null)
        {
            string sm = shakeItem.Content.ToString()!;
            if (sm.Contains("Navigation", StringComparison.OrdinalIgnoreCase))
            {
                _settings.ShakeMode = "Navigation";
                _settings.EnableShakeClicks = true;
            }
            else if (sm.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                _settings.ShakeMode = "Disabled";
                _settings.EnableShakeClicks = false;
            }
            else
            {
                _settings.ShakeMode = "Visual";
                _settings.EnableShakeClicks = true;
            }
            _engine.Config.ShakeMode = _settings.ShakeMode;
            _engine.Config.EnableShakeClicks = _settings.EnableShakeClicks;
        }

        _settings.EnableAntiAfk = ChkAntiAfk.IsChecked == true;
        _engine.Config.EnableAntiAfk = _settings.EnableAntiAfk;

        _settings.EnableHumanizedJitter = ChkJitter.IsChecked == true;
        _engine.Config.EnableHumanizedJitter = _settings.EnableHumanizedJitter;

        _settings.EnableDynamicCastRelease = true;
        _engine.Config.EnableDynamicCastRelease = true;

        _settings.EnableAutoClaimAquarium = ChkAutoClaimAquarium.IsChecked == true;
        _engine.Config.EnableAutoClaimAquarium = _settings.EnableAutoClaimAquarium;

        if (int.TryParse(TxtAquariumInterval.Text, out int ai) && ai >= 5)
        {
            _settings.AquariumClaimIntervalMinutes = ai;
            _engine.Config.AquariumClaimIntervalMinutes = ai;
        }

        _settings.EnableAutoOpenCrates = ChkAutoOpenCrates.IsChecked == true;
        _engine.Config.EnableAutoOpenCrates = _settings.EnableAutoOpenCrates;

        _settings.AutoRunPreFlightOnStart = ChkAutoPreFlight.IsChecked == true;

        if (int.TryParse(TxtCrateInterval.Text, out int ci) && ci >= 1)
        {
            _settings.CrateIntervalCatches = ci;
            _engine.Config.CrateIntervalCatches = ci;
        }

        if (int.TryParse(TxtCrateMaxTypes.Text, out int cmt) && cmt >= 0 && cmt <= 999)
        {
            _settings.CrateMaxTypes = cmt;
            _engine.Config.CrateMaxTypes = cmt;
        }

        if (int.TryParse(TxtMaxRecordings.Text, out int mr) && mr > 0)
        {
            _settings.MaxRecordingsToKeep = mr;
            _engine.Recorder.MaxRecordingsToKeep = mr;
        }

        if (CmbMinigameTheme.SelectedItem is ComboBoxItem themeItem)
        {
            _settings.SelectedTheme = (MinigameTheme)CmbMinigameTheme.SelectedIndex;
            _engine.Config.SelectedTheme = _settings.SelectedTheme;
        }

        _settings.Save();
        RegisterHotkeys();

        MessageBox.Show($"Configuration saved!\nCasting: 100% Dynamic Vision Auto-Cast\nAuto-Shake: [{_settings.ShakeMode}]\nAnti-AFK Kick: [{(_settings.EnableAntiAfk ? "Enabled" : "Disabled")}]\nHuman Jitter: [{(_settings.EnableHumanizedJitter ? "Enabled" : "Disabled")}]\nAuto-Claim Aquarium: [{(_settings.EnableAutoClaimAquarium ? $"Every {_settings.AquariumClaimIntervalMinutes}m" : "Disabled")}]\nAuto-Open Crates: [{(_settings.EnableAutoOpenCrates ? $"Every {_settings.CrateIntervalCatches} catches (max {_settings.CrateMaxTypes} types)" : "Disabled")}]\nStart/Stop Hotkey: [{_settings.ToggleHotkey}]\nRe-equip Hotkey: [{_settings.ReEquipHotkey}]\nTheme: [{_settings.SelectedTheme}]", "Fat Dad's Fisch AFK Pro", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CmbMinigameTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CmbMinigameTheme == null || _settings == null || _engine == null) return;
        _settings.SelectedTheme = (MinigameTheme)CmbMinigameTheme.SelectedIndex;
        _engine.Config.SelectedTheme = _settings.SelectedTheme;
    }

    private void BtnResetStats_Click(object sender, RoutedEventArgs e)
    {
        _engine.ResetStats();
        TxtTotalCatches.Text = "0";
        TxtCatchRate.Text = "0.0/hr";
        TxtWinRate.Text = "100%";
        TxtSessionUptime.Text = "00:00:00";
        TxtStreakBadge.Text = "🔥 Streak: 0";
    }

    private void ChkAntiAfk_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _engine == null) return;
        bool val = ChkAntiAfk.IsChecked == true;
        _settings.EnableAntiAfk = val;
        _engine.Config.EnableAntiAfk = val;
    }

    private void ChkJitter_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _engine == null) return;
        bool val = ChkJitter.IsChecked == true;
        _settings.EnableHumanizedJitter = val;
        _engine.Config.EnableHumanizedJitter = val;
    }

    private void ChkAutoClaimAquarium_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _engine == null) return;
        bool val = ChkAutoClaimAquarium.IsChecked == true;
        _settings.EnableAutoClaimAquarium = val;
        _engine.Config.EnableAutoClaimAquarium = val;
    }

    private void ChkAutoOpenCrates_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _engine == null) return;
        bool val = ChkAutoOpenCrates.IsChecked == true;
        _settings.EnableAutoOpenCrates = val;
        _engine.Config.EnableAutoOpenCrates = val;
    }



    private async void BtnClaimAquarium_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null) return;
        if (_engine.IsRunning)
        {
            MessageBox.Show("Fishing is currently running!\n\nPlease stop fishing [F6] before running standalone Aquarium claim.", "Claim Aquarium", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var btn = sender as Button;
        string origContent = btn?.Content?.ToString() ?? "Claim";
        if (btn != null)
        {
            btn.IsEnabled = false;
            btn.Content = "Claiming...";
        }

        if (BtnToggle != null)
        {
            BtnToggle.IsEnabled = false;
            BtnToggle.Opacity = 0.5;
        }

        bool success = false;
        try
        {
            await Task.Run(() =>
            {
                success = _engine.ExecuteAquariumClaim();
            });
        }
        finally
        {
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Content = origContent;
            }
            if (BtnToggle != null)
            {
                BtnToggle.IsEnabled = true;
                BtnToggle.Opacity = 1.0;
            }
        }

        if (!success)
        {
            MessageBox.Show("Roblox window not detected! Please ensure Roblox is running and in windowed mode.", "Aquarium Auto-Claim", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            string recMsg = !string.IsNullOrEmpty(_engine.LastAquariumReplicationDir)
                ? $"\n\nDiagnostic snapshots saved to:\n{_engine.LastAquariumReplicationDir}"
                : "";
            MessageBox.Show($"🏆 Aquarium profit successfully claimed! Modal closed and rod re-equipped.{recMsg}", "Aquarium Auto-Claim", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ChkAutoPreFlight_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _settings == null) return;
        _settings.AutoRunPreFlightOnStart = ChkAutoPreFlight.IsChecked == true;
        _settings.Save();
    }

    private void BtnClosePreFlight_Click(object sender, RoutedEventArgs e)
    {
        BorderPreFlightResults.Visibility = Visibility.Collapsed;
    }

    private bool _isDiagnosticRunning = false;

    private async void BtnPreFlight_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning || _isDiagnosticRunning) return;
        await RunPreFlightDiagnosticAsync();
    }

    private async Task<bool> RunPreFlightDiagnosticAsync()
    {
        if (_isDiagnosticRunning) return false;
        _isDiagnosticRunning = true;

        BtnPreFlight.IsEnabled = false;
        BtnPreFlight.Opacity = 0.5;
        BtnToggle.IsEnabled = false;
        BtnToggle.Opacity = 0.5;

        BorderPreFlightResults.Visibility = Visibility.Visible;
        TxtPreFlightDuration.Text = "(Running...)";

        // Reset Verdict Banner to Running Blue
        BannerPreFlightVerdict.Background = new SolidColorBrush(Color.FromRgb(15, 56, 84));
        BannerPreFlightVerdict.BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199));
        TxtPreFlightVerdictIcon.Text = "⏳";
        TxtPreFlightVerdict.Text = "RUNNING IN-GAME PRE-FLIGHT DIAGNOSTIC...";
        TxtPreFlightVerdict.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));

        // Reset step items to pending
        ResetStepUI(StepItemWindow, IconStepWindow, MetricStepWindow, DescStepWindow, "Checking for active Roblox client...");
        ResetStepUI(StepItemCapture, IconStepCapture, MetricStepCapture, DescStepCapture, "Measuring BitBlt latency (target sub-3ms)...");
        ResetStepUI(StepItemHotbar, IconStepHotbar, MetricStepHotbar, DescStepHotbar, "Locating CoreGui hotbar container...");
        ResetStepUI(StepItemToggle, IconStepToggle, MetricStepToggle, DescStepToggle, "Sending momentary '1' keypress and verifying CV detection...");
        ResetStepUI(StepItemSafety, IconStepSafety, MetricStepSafety, DescStepSafety, "Auditing water target, slot click, and dialog coordinates...");

        using var diag = new PreFlightDiagnostic(_settings);

        PreFlightReport report = await Task.Run(async () =>
        {
            return await diag.RunDiagnosticAsync(step =>
            {
                Dispatcher.Invoke(() => UpdateStepUI(step));
            });
        });

        // Update overall verdict
        TxtPreFlightDuration.Text = $"({report.TotalDurationMs / 1000.0:F1}s)";

        if (report.OverallPass)
        {
            BannerPreFlightVerdict.Background = new SolidColorBrush(Color.FromRgb(12, 46, 36));
            BannerPreFlightVerdict.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtPreFlightVerdictIcon.Text = "✅";
            TxtPreFlightVerdict.Text = "ALL SYSTEMS NOMINAL — READY FOR UNATTENDED AFK 🎣";
            TxtPreFlightVerdict.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        }
        else
        {
            BannerPreFlightVerdict.Background = new SolidColorBrush(Color.FromRgb(69, 26, 26));
            BannerPreFlightVerdict.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtPreFlightVerdictIcon.Text = "⚠️";
            TxtPreFlightVerdict.Text = "ATTENTION: " + report.Summary;
            TxtPreFlightVerdict.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }

        // Render annotated snapshot on live preview monitor if available
        if (report.AnnotatedSnapshot != null && !report.AnnotatedSnapshot.Empty())
        {
            RenderPreviewFrame(report.AnnotatedSnapshot);
            report.AnnotatedSnapshot.Dispose();
        }

        BtnPreFlight.IsEnabled = true;
        BtnPreFlight.Opacity = 1.0;
        BtnToggle.IsEnabled = true;
        BtnToggle.Opacity = 1.0;
        _isDiagnosticRunning = false;

        return report.OverallPass;
    }

    private void ResetStepUI(Border item, TextBlock icon, TextBlock metric, TextBlock desc, string defaultDesc)
    {
        item.Background = new SolidColorBrush(Color.FromRgb(17, 22, 34));
        item.BorderBrush = new SolidColorBrush(Color.FromRgb(28, 38, 56));
        icon.Text = "⏳";
        metric.Text = "Pending...";
        metric.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        desc.Text = defaultDesc;
    }

    private void UpdateStepUI(DiagnosticStep step)
    {
        Border item;
        TextBlock icon;
        TextBlock metric;
        TextBlock desc;

        switch (step.Id)
        {
            case "window":
                item = StepItemWindow; icon = IconStepWindow; metric = MetricStepWindow; desc = DescStepWindow;
                break;
            case "capture":
                item = StepItemCapture; icon = IconStepCapture; metric = MetricStepCapture; desc = DescStepCapture;
                break;
            case "hotbar":
                item = StepItemHotbar; icon = IconStepHotbar; metric = MetricStepHotbar; desc = DescStepHotbar;
                break;
            case "tool_toggle":
                item = StepItemToggle; icon = IconStepToggle; metric = MetricStepToggle; desc = DescStepToggle;
                break;
            case "coordinates":
                item = StepItemSafety; icon = IconStepSafety; metric = MetricStepSafety; desc = DescStepSafety;
                break;
            default:
                return;
        }

        if (step.Status == DiagnosticStatus.Running)
        {
            item.Background = new SolidColorBrush(Color.FromRgb(15, 31, 46));
            item.BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199));
            icon.Text = "⏳";
            metric.Text = "Testing...";
            metric.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        }
        else if (step.Status == DiagnosticStatus.Pass)
        {
            item.Background = new SolidColorBrush(Color.FromRgb(10, 31, 24));
            item.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            icon.Text = "✅";
            metric.Text = step.Metric;
            metric.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            desc.Text = step.Details;
        }
        else if (step.Status == DiagnosticStatus.Warning)
        {
            item.Background = new SolidColorBrush(Color.FromRgb(36, 26, 10));
            item.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            icon.Text = "⚠️";
            metric.Text = step.Metric;
            metric.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            desc.Text = step.Details;
        }
        else if (step.Status == DiagnosticStatus.Fail)
        {
            item.Background = new SolidColorBrush(Color.FromRgb(46, 16, 16));
            item.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            icon.Text = "❌";
            metric.Text = step.Metric;
            metric.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            desc.Text = step.Details;
        }
    }

    private void RenderPreviewFrame(Mat? frame)
    {
        if (frame == null || frame.Empty()) return;

        try
        {
            int fw = frame.Width;
            int fh = frame.Height;

            if (_previewBitmap == null || _previewBitmap.PixelWidth != fw || _previewBitmap.PixelHeight != fh)
            {
                _previewBitmap = new WriteableBitmap(fw, fh, 96, 96, PixelFormats.Bgr24, null);
                ImgPreview.Source = _previewBitmap;
            }

            if (TxtPreviewPlaceholder.Visibility != Visibility.Collapsed)
            {
                TxtPreviewPlaceholder.Visibility = Visibility.Collapsed;
            }

            _previewBitmap.Lock();
            try
            {
                unsafe
                {
                    int srcStride = (int)frame.Step();
                    int dstStride = _previewBitmap.BackBufferStride;
                    byte* pSrc = (byte*)frame.Data;
                    byte* pDst = (byte*)_previewBitmap.BackBuffer;

                    if (srcStride == dstStride)
                    {
                        long totalBytes = (long)dstStride * fh;
                        Buffer.MemoryCopy(pSrc, pDst, totalBytes, totalBytes);
                    }
                    else
                    {
                        int bytesPerRow = Math.Min(srcStride, dstStride);
                        for (int r = 0; r < fh; r++)
                        {
                            Buffer.MemoryCopy(pSrc + (r * srcStride), pDst + (r * dstStride), bytesPerRow, bytesPerRow);
                        }
                    }
                }
                _previewBitmap.AddDirtyRect(new Int32Rect(0, 0, fw, fh));
            }
            finally
            {
                _previewBitmap.Unlock();
            }
        }
        catch { }
    }

    private CancellationTokenSource? _crateCts;

    private async void BtnOpenCrates_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null) return;
        if (_engine.IsRunning)
        {
            MessageBox.Show("Fishing is currently running!\n\nPlease stop fishing [F6] before running standalone Crate unpack.", "Open Crates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // If currently running, stop immediately
        if (_crateCts != null)
        {
            _crateCts.Cancel();
            if (BtnOpenCrates != null) BtnOpenCrates.Content = "Stopping...";
            if (BtnTestOpenCrates != null) BtnTestOpenCrates.Content = "Stopping...";
            return;
        }

        int maxTypes = 25;
        if (int.TryParse(TxtCrateMaxTypes?.Text, out int m) && m >= 0 && m <= 999) maxTypes = m;

        _crateCts = new CancellationTokenSource();
        var ct = _crateCts.Token;

        var origOpenContent = BtnOpenCrates?.Content;
        var origTestContent = BtnTestOpenCrates?.Content;

        if (BtnOpenCrates != null)
        {
            BtnOpenCrates.Content = "⏹ Stop";
            BtnOpenCrates.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29)); // Crimson red
        }
        if (BtnTestOpenCrates != null)
        {
            BtnTestOpenCrates.Content = "⏹ Stop";
            BtnTestOpenCrates.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
        }

        if (BtnToggle != null)
        {
            BtnToggle.IsEnabled = false;
            BtnToggle.Opacity = 0.5;
        }

        bool success = false;

        try
        {
            await Task.Run(() =>
            {
                success = _engine.ExecuteAutoOpenCrates(maxTypes, (progress) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (TxtAction != null)
                        {
                            TxtAction.Text = progress;
                        }
                    });
                }, ct);
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _crateCts?.Dispose();
            _crateCts = null;

            if (BtnOpenCrates != null)
            {
                BtnOpenCrates.Content = "📦 Open Crates";
                BtnOpenCrates.Background = new SolidColorBrush(Color.FromRgb(33, 21, 8));
            }
            if (BtnTestOpenCrates != null)
            {
                BtnTestOpenCrates.Content = "Open Now";
                BtnTestOpenCrates.Background = new SolidColorBrush(Color.FromRgb(46, 27, 14));
            }

            if (BtnToggle != null)
            {
                BtnToggle.IsEnabled = true;
                BtnToggle.Opacity = 1.0;
            }

            _ = Task.Delay(3500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_engine != null && !_engine.IsRunning && TxtAction != null)
                    {
                        TxtAction.Text = "Ready";
                    }
                });
            });
        }

        if (!success && !ct.IsCancellationRequested)
        {
            MessageBox.Show("Roblox window not detected! Please ensure Roblox is running.", "Auto Crate Opener", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }



    private void CmbShakeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null) return;
        if (CmbShakeMode?.SelectedItem is ComboBoxItem item && item.Content != null)
        {
            string mode = item.Content.ToString()!;
            if (mode.Contains("Navigation", StringComparison.OrdinalIgnoreCase))
            {
                _settings.ShakeMode = "Navigation";
                _settings.EnableShakeClicks = true;
            }
            else if (mode.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                _settings.ShakeMode = "Disabled";
                _settings.EnableShakeClicks = false;
            }
            else
            {
                _settings.ShakeMode = "Visual";
                _settings.EnableShakeClicks = true;
            }

            if (_engine != null)
            {
                _engine.Config.ShakeMode = _settings.ShakeMode;
                _engine.Config.EnableShakeClicks = _settings.EnableShakeClicks;
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_startup.log");
        try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] MainWindow_Loaded start\n"); } catch { }

        _ = CheckForUpdatesAsync();

        await System.Threading.Tasks.Task.Delay(2600); // Display startup splash for 2.6s
        DismissSplash();
        try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] MainWindow_Loaded splash dismissed\n"); } catch { }
    }

    private string _latestReleaseUrl = "https://github.com/gmoney887/fisch-afk/releases/latest";

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(4);
            client.DefaultRequestHeaders.Add("User-Agent", "FatDadsFischAFK");

            string apiUrl = "https://api.github.com/repos/gmoney887/fisch-afk/releases/latest";
            var response = await client.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode) return;

            string json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElem)) return;
            string? tagName = tagElem.GetString()?.Trim();
            if (string.IsNullOrEmpty(tagName)) return;

            if (root.TryGetProperty("html_url", out var urlElem))
            {
                string? htmlUrl = urlElem.GetString();
                if (!string.IsNullOrEmpty(htmlUrl))
                    _latestReleaseUrl = htmlUrl;
            }

            // Parse version strings for semantic comparison
            string remoteVerClean = tagName.TrimStart('v', 'V').Split('-')[0];
            var localVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (localVer == null) return;

            if (Version.TryParse(remoteVerClean, out var remoteVer))
            {
                Version normRemote = new Version(Math.Max(0, remoteVer.Major), Math.Max(0, remoteVer.Minor), Math.Max(0, remoteVer.Build));
                Version normLocal = new Version(Math.Max(0, localVer.Major), Math.Max(0, localVer.Minor), Math.Max(0, localVer.Build));

                if (normRemote > normLocal)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtUpdateBanner.Text = $"🚀 Update Available: {tagName} — Click to download";
                        BorderUpdateBanner.Visibility = Visibility.Visible;
                    });
                }
            }
        }
        catch
        {
            // Silently ignore network / rate-limit failures
        }
    }

    private void BorderUpdateBanner_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_latestReleaseUrl)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OverlaySplash_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DismissSplash();
    }

    private void DismissSplash()
    {
        if (OverlaySplash.Visibility != Visibility.Visible) return;
        OverlaySplash.Visibility = Visibility.Collapsed;
    }

    private void Avatar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OverlaySplash.Visibility = Visibility.Visible;
        TxtSplashStatus.Text = $"Active Rod Pull Power: {_engine.EstimatedRodPull:F0} px/s²";
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_startup.log");
        try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Window_Closing called! StackTrace:\n{Environment.StackTrace}\n"); } catch { }

        _keyboardHook?.Dispose();
        Win32.UnregisterHotKey(_hwnd, HOTKEY_ID_TOGGLE);
        Win32.UnregisterHotKey(_hwnd, HOTKEY_ID_REEQUIP);
        Win32.UnregisterHotKey(_hwnd, HOTKEY_ID_END);
        _engine.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}