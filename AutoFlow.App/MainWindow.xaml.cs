using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AutoFlow.App.Services;
using AutoFlow.App.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AutoFlow.App;

public partial class MainWindow : Window
{
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly GlobalMouseHookService _mouseHookService;
    private readonly WindowControlService _windowControlService;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainWindowViewModel(Close, OpenSettingsWindow);
        Style = (Style)FindResource(typeof(Window));
        SourceInitialized += MainWindow_OnSourceInitialized;
        DataContext = ViewModel;
        _windowControlService = new WindowControlService(this, OnHotkeysChanged);

        _hotkeyService = new GlobalHotkeyService(ViewModel.AppendLogMessage);
        _mouseHookService = new GlobalMouseHookService(ViewModel.AppendLogMessage);
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        SubscribeInputServices();
    }

    public MainWindowViewModel ViewModel { get; }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowControlService.ApplyStartupPlacement();
        _hotkeyService.Initialize(this);
        _hotkeyService.SetScreenToolShortcutsEnabled(ViewModel.IsScreenToolVisible);
        _mouseHookService.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        UnsubscribeInputServices();
        _hotkeyService.Dispose();
        _mouseHookService.Dispose();
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_windowControlService.HandleMainWindowClosing())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_hotkeyService.HandlePreviewKeyDown(e.Key))
        {
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        _hotkeyService.HandlePreviewKeyUp(e.Key);

        base.OnPreviewKeyUp(e);
    }

    private void OpenSettingsWindow()
    {
        _windowControlService.ToggleSettingsWindow();
    }

    private void OnHotkeysChanged()
    {
        _hotkeyService.ReloadConfiguredHotkeys(showFailureMessage: true);
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsScreenToolVisible))
        {
            _hotkeyService.SetScreenToolShortcutsEnabled(ViewModel.IsScreenToolVisible);
        }
    }

    private void HandleScreenToolRecordRequested()
    {
        ViewModel.AppendLogMessage(ScreenToolPopupControl.CreateCurrentReadingLogMessage());
    }

    private void HandleScreenToolColorDisplayToggleRequested()
    {
        ScreenToolPopupControl.ToggleColorDisplayFormat();
    }

    public void PrepareForExit()
    {
        _hotkeyService.CleanupAllRegistrations();
        _windowControlService.PrepareForExit();
    }

    private void SubscribeInputServices()
    {
        _hotkeyService.RunRequested += ViewModel.ExecuteRunCommand;
        _hotkeyService.StopRequested += ViewModel.ExecuteStopCommand;
        _hotkeyService.RecordRequested += ViewModel.ExecuteRecordCommand;
        _hotkeyService.ScreenToolToggleRequested += ViewModel.ExecuteToggleScreenToolCommand;
        _hotkeyService.ScreenToolRecordRequested += HandleScreenToolRecordRequested;
        _hotkeyService.ScreenToolColorDisplayToggleRequested += HandleScreenToolColorDisplayToggleRequested;
        _hotkeyService.KeyboardInputObserved += ViewModel.HandleObservedKeyboardInput;

        _mouseHookService.MouseMoved += ScreenToolPopupControl.OnGlobalMouseMove;
        _mouseHookService.ShortcutMouseButtonDown += _hotkeyService.HandleGlobalMouseButtonDown;
        _mouseHookService.ShortcutMouseButtonUp += _hotkeyService.HandleGlobalMouseButtonUp;
        _mouseHookService.MouseButtonObserved += ViewModel.HandleObservedMouseInput;
    }

    private void UnsubscribeInputServices()
    {
        _hotkeyService.RunRequested -= ViewModel.ExecuteRunCommand;
        _hotkeyService.StopRequested -= ViewModel.ExecuteStopCommand;
        _hotkeyService.RecordRequested -= ViewModel.ExecuteRecordCommand;
        _hotkeyService.ScreenToolToggleRequested -= ViewModel.ExecuteToggleScreenToolCommand;
        _hotkeyService.ScreenToolRecordRequested -= HandleScreenToolRecordRequested;
        _hotkeyService.ScreenToolColorDisplayToggleRequested -= HandleScreenToolColorDisplayToggleRequested;
        _hotkeyService.KeyboardInputObserved -= ViewModel.HandleObservedKeyboardInput;

        _mouseHookService.MouseMoved -= ScreenToolPopupControl.OnGlobalMouseMove;
        _mouseHookService.ShortcutMouseButtonDown -= _hotkeyService.HandleGlobalMouseButtonDown;
        _mouseHookService.ShortcutMouseButtonUp -= _hotkeyService.HandleGlobalMouseButtonUp;
        _mouseHookService.MouseButtonObserved -= ViewModel.HandleObservedMouseInput;
    }
}
