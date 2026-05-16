using System.Windows.Input;

namespace AutoFlow.App.Models;

public readonly record struct RunRequestedMessage;

public readonly record struct StopRequestedMessage;

public readonly record struct RecordRequestedMessage;

public readonly record struct ScreenToolToggleRequestedMessage;

public readonly record struct ScreenToolRecordRequestedMessage;

public readonly record struct ScreenToolColorDisplayToggleRequestedMessage;

public readonly record struct HotkeysReloadRequestedMessage;

public readonly record struct ToggleSettingsWindowRequestedMessage;

public readonly record struct CloseMainWindowRequestedMessage;

public readonly record struct ShowMainWindowRequestedMessage;

public readonly record struct ExitApplicationRequestedMessage;

public readonly record struct TrayInfoRequestedMessage(string Title, string Message, int TimeoutMilliseconds = 3000);

public readonly record struct KeyboardInputObservedMessage(Key Key, bool IsKeyDown);

public readonly record struct MouseMovedMessage(int X, int Y);

public readonly record struct MouseButtonObservedMessage(string Button, bool IsButtonDown, int X, int Y);
