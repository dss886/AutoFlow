using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AutoFlow.App.Infrastructure;
using AutoFlow.App.Services;
using AutoFlow.App.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AutoFlow.App;

public partial class MainWindow : Window
{
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly GlobalMouseHookService _mouseHookService;
    private readonly WindowControlService _windowControlService;

    public MainWindow(
        MainWindowViewModel viewModel,
        GlobalHotkeyService hotkeyService,
        GlobalMouseHookService mouseHookService,
        IEventBus eventBus,
        AppLoggerService logger,
        WindowControlService windowControlService,
        LocalSettingsService localSettingsService,
        ScreenColorService screenColorService)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(hotkeyService);
        ArgumentNullException.ThrowIfNull(mouseHookService);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(windowControlService);
        ArgumentNullException.ThrowIfNull(localSettingsService);
        ArgumentNullException.ThrowIfNull(screenColorService);

        Style = (Style)FindResource(typeof(Window));
        SourceInitialized += MainWindow_OnSourceInitialized;

        ViewModel = viewModel;
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        DataContext = ViewModel;

        _hotkeyService = hotkeyService;
        _mouseHookService = mouseHookService;
        _windowControlService = windowControlService;
        _windowControlService.Initialize(this);
        ScreenToolPopupControl.Initialize(eventBus, logger, localSettingsService, screenColorService);

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
        ScreenToolPopupControl.DisposeSubscriptions();
        _windowControlService.Dispose();
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

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsScreenToolVisible))
        {
            _hotkeyService.SetScreenToolShortcutsEnabled(ViewModel.IsScreenToolVisible);
        }
    }

    public void PrepareForExit()
    {
        _hotkeyService.CleanupAllRegistrations();
        _windowControlService.PrepareForExit();
    }

    private void SubscribeInputServices()
    {
        _mouseHookService.ShortcutMouseButtonDown += _hotkeyService.HandleGlobalMouseButtonDown;
        _mouseHookService.ShortcutMouseButtonUp += _hotkeyService.HandleGlobalMouseButtonUp;
    }

    private void UnsubscribeInputServices()
    {
        _mouseHookService.ShortcutMouseButtonDown -= _hotkeyService.HandleGlobalMouseButtonDown;
        _mouseHookService.ShortcutMouseButtonUp -= _hotkeyService.HandleGlobalMouseButtonUp;
    }
}
