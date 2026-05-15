using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoFlow.App.Models;
using AutoFlow.App.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace AutoFlow.App.Views;

public partial class SettingsWindow : Window
{
    private readonly Action _hotkeysChanged;
    private ShortcutBindingItem? _capturingItem;

    public SettingsWindow(Action hotkeysChanged)
    {
        _hotkeysChanged = hotkeysChanged ?? throw new ArgumentNullException(nameof(hotkeysChanged));
        InitializeComponent();
        Bindings = CreateBindings(LocalSettingsService.LoadHotkeySettings());
        DataContext = this;
    }

    public ObservableCollection<ShortcutBindingItem> Bindings { get; }

    private void RebindButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShortcutBindingItem item })
        {
            SetCapturingItem(item);
        }
    }

    private void ResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ShortcutBindingItem item })
        {
            return;
        }

        if (!TryApplyShortcut(item, item.DefaultShortcut))
        {
            return;
        }

        if (ReferenceEquals(item, _capturingItem))
        {
            SetCapturingItem(null);
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingItem is null)
        {
            return;
        }

        e.Handled = true;

        var key = NormalizeKey(e);
        if (key == Key.Escape)
        {
            SetCapturingItem(null);
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        _ = TryApplyShortcut(_capturingItem, new ShortcutGesture(key, Keyboard.Modifiers));
        SetCapturingItem(null);
    }

    private void Window_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_capturingItem is null)
        {
            return;
        }

        if (!TryConvertMouseButton(e.ChangedButton, out var mouseButton))
        {
            return;
        }

        e.Handled = true;
        _ = TryApplyShortcut(_capturingItem, ShortcutGesture.FromMouseGesture(mouseButton, Keyboard.Modifiers));
        SetCapturingItem(null);
    }

    private static ObservableCollection<ShortcutBindingItem> CreateBindings(AppHotkeySettings hotkeySettings)
    {
        var defaults = AppHotkeySettings.CreateDefault();
        return
        [
            new ShortcutBindingItem(ShortcutBindingKey.Run, "运行", hotkeySettings.Run, defaults.Run),
            new ShortcutBindingItem(ShortcutBindingKey.Stop, "停止", hotkeySettings.Stop, defaults.Stop),
            new ShortcutBindingItem(ShortcutBindingKey.Record, "录制", hotkeySettings.Record, defaults.Record),
            new ShortcutBindingItem(ShortcutBindingKey.ScreenTool, "屏幕工具", hotkeySettings.ScreenTool, defaults.ScreenTool),
        ];
    }

    private ShortcutGesture GetShortcut(ShortcutBindingKey key)
    {
        return Bindings.First(item => item.Key == key).Shortcut;
    }

    private bool TryApplyShortcut(ShortcutBindingItem item, ShortcutGesture shortcut)
    {
        var duplicate = Bindings.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, item)
            && !shortcut.IsEmpty
            && candidate.Shortcut == shortcut);

        if (duplicate is not null)
        {
            MessageBox.Show(
                $"快捷键 {shortcut.DisplayText} 已绑定给“{duplicate.DisplayName}”，请换一个组合键。",
                "快捷键设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        item.Shortcut = shortcut;
        LocalSettingsService.SaveHotkeySettings(new AppHotkeySettings
        {
            Run = GetShortcut(ShortcutBindingKey.Run),
            Stop = GetShortcut(ShortcutBindingKey.Stop),
            Record = GetShortcut(ShortcutBindingKey.Record),
            ScreenTool = GetShortcut(ShortcutBindingKey.ScreenTool),
        });
        _hotkeysChanged();
        return true;
    }

    private void SetCapturingItem(ShortcutBindingItem? item)
    {
        if (_capturingItem is not null)
        {
            _capturingItem.IsCapturing = false;
        }

        _capturingItem = item;
        if (_capturingItem is null)
        {
            return;
        }

        _capturingItem.IsCapturing = true;
        Activate();
        Focus();
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
    }

    private static Key NormalizeKey(KeyEventArgs e)
    {
        return e.Key == Key.System ? e.SystemKey : e.Key;
    }

    private static bool TryConvertMouseButton(MouseButton mouseButton, out ShortcutMouseButton shortcutMouseButton)
    {
        shortcutMouseButton = mouseButton switch
        {
            MouseButton.Middle => ShortcutMouseButton.Middle,
            MouseButton.XButton1 => ShortcutMouseButton.XButton1,
            MouseButton.XButton2 => ShortcutMouseButton.XButton2,
            _ => ShortcutMouseButton.None,
        };

        return shortcutMouseButton != ShortcutMouseButton.None;
    }

    public sealed class ShortcutBindingItem : INotifyPropertyChanged
    {
        private ShortcutGesture _shortcut;
        private bool _isCapturing;

        public ShortcutBindingItem(ShortcutBindingKey key, string displayName, ShortcutGesture shortcut, ShortcutGesture defaultShortcut)
        {
            Key = key;
            DisplayName = displayName;
            _shortcut = shortcut;
            DefaultShortcut = defaultShortcut;
        }

        public ShortcutBindingKey Key { get; }

        public string DisplayName { get; }

        public ShortcutGesture DefaultShortcut { get; }

        public string DefaultText => $"默认: {DefaultShortcut.DisplayText}";

        public ShortcutGesture Shortcut
        {
            get => _shortcut;
            set
            {
                if (_shortcut == value)
                {
                    return;
                }

                _shortcut = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShortcutText));
            }
        }

        public bool IsCapturing
        {
            get => _isCapturing;
            set
            {
                if (_isCapturing == value)
                {
                    return;
                }

                _isCapturing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShortcutText));
            }
        }

        public string ShortcutText => IsCapturing ? "请按下快捷键" : Shortcut.DisplayText;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum ShortcutBindingKey
    {
        Run,
        Stop,
        Record,
        ScreenTool,
    }
}
