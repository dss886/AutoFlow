using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using AutoFlow.App.Services;

namespace AutoFlow.App.Sessions;

public sealed class InputRecordingSession
{
    private const int TapThresholdMilliseconds = 220;
    private const int ClickThresholdMilliseconds = 220;
    private const int ClickMoveThresholdPixels = 4;

    private readonly ScreenColorService _screenColorService;
    private readonly List<RawInputEvent> _events = new();
    private readonly Stopwatch _stopwatch = new();
    private bool _isRecording;
    private long _nextSequence;

    public InputRecordingSession(ScreenColorService screenColorService)
    {
        _screenColorService = screenColorService ?? throw new ArgumentNullException(nameof(screenColorService));
    }

    public bool IsRecording => _isRecording;

    public void Start()
    {
        _events.Clear();
        _nextSequence = 0;
        _isRecording = true;
        _stopwatch.Restart();
    }

    public string StopAndBuildScript(string scriptName, string description)
    {
        if (!_isRecording)
        {
            throw new InvalidOperationException("录制尚未开始。");
        }

        var stopTimestamp = _stopwatch.ElapsedMilliseconds;
        _stopwatch.Stop();
        _isRecording = false;

        var actions = BuildScriptActions(stopTimestamp);
        return RenderScript(scriptName, description, actions);
    }

    public bool RecordMouseButtonEvent(string button, bool isButtonDown, int x, int y)
    {
        if (!_isRecording || !IsSupportedMouseButton(button))
        {
            return false;
        }

        _events.Add(new RawMouseInputEvent(
            Timestamp: _stopwatch.ElapsedMilliseconds,
            Sequence: _nextSequence++,
            Button: NormalizeMouseButton(button),
            X: x,
            Y: y,
            IsButtonDown: isButtonDown));

        return true;
    }

    public bool RecordKeyboardEvent(Key key, bool isKeyDown)
    {
        if (!_isRecording || key is Key.None or Key.System)
        {
            return false;
        }

        _events.Add(new RawKeyboardInputEvent(
            Timestamp: _stopwatch.ElapsedMilliseconds,
            Sequence: _nextSequence++,
            Key: NormalizeKey(key),
            IsKeyDown: isKeyDown));

        return true;
    }

    public CursorReading CaptureCursorReading()
    {
        if (!GetCursorPos(out var point))
        {
            return new CursorReading(0, 0, _screenColorService.FormatHexColor(System.Windows.Media.Colors.White));
        }

        return new CursorReading(
            point.X,
            point.Y,
            _screenColorService.GetScreenColorHex(point.X, point.Y));
    }

    public string CreateReadingLogMessage(int x, int y)
    {
        return $"鼠标位置 ({x}, {y}), 颜色 {_screenColorService.GetScreenColorHex(x, y)}";
    }

    private List<ScriptAction> BuildScriptActions(long stopTimestamp)
    {
        var actions = new List<ScriptAction>();
        actions.AddRange(BuildMouseActions(stopTimestamp));
        actions.AddRange(BuildKeyboardActions(stopTimestamp));

        return actions
            .OrderBy(action => action.Timestamp)
            .ThenBy(action => action.OrderKey)
            .ToList();
    }

    private List<ScriptAction> BuildMouseActions(long stopTimestamp)
    {
        var actions = new List<ScriptAction>();
        var pendingByButton = new Dictionary<string, RawMouseInputEvent>(StringComparer.OrdinalIgnoreCase);

        foreach (var mouseEvent in _events.OfType<RawMouseInputEvent>())
        {
            if (mouseEvent.IsButtonDown)
            {
                pendingByButton[mouseEvent.Button] = mouseEvent;
                continue;
            }

            if (!pendingByButton.Remove(mouseEvent.Button, out var downEvent))
            {
                continue;
            }

            var duration = mouseEvent.Timestamp - downEvent.Timestamp;
            var distance = Math.Abs(mouseEvent.X - downEvent.X) + Math.Abs(mouseEvent.Y - downEvent.Y);
            if (duration <= ClickThresholdMilliseconds && distance <= ClickMoveThresholdPixels)
            {
                actions.Add(new ScriptAction(
                    downEvent.Timestamp,
                    downEvent.Sequence * 10,
                    ScriptActionType.MouseClick,
                    downEvent.Button,
                    downEvent.X,
                    downEvent.Y));
                continue;
            }

            actions.Add(new ScriptAction(
                downEvent.Timestamp,
                downEvent.Sequence * 10,
                ScriptActionType.MouseDown,
                downEvent.Button,
                downEvent.X,
                downEvent.Y));

            if (downEvent.X != mouseEvent.X || downEvent.Y != mouseEvent.Y)
            {
                actions.Add(new ScriptAction(
                    mouseEvent.Timestamp,
                    mouseEvent.Sequence * 10,
                    ScriptActionType.MouseMove,
                    null,
                    mouseEvent.X,
                    mouseEvent.Y));
            }

            actions.Add(new ScriptAction(
                mouseEvent.Timestamp,
                mouseEvent.Sequence * 10 + 1,
                ScriptActionType.MouseUp,
                mouseEvent.Button,
                mouseEvent.X,
                mouseEvent.Y));
        }

        foreach (var pending in pendingByButton.Values)
        {
            actions.Add(new ScriptAction(
                pending.Timestamp,
                pending.Sequence * 10,
                ScriptActionType.MouseDown,
                pending.Button,
                pending.X,
                pending.Y));
            actions.Add(new ScriptAction(
                stopTimestamp,
                pending.Sequence * 10 + 1,
                ScriptActionType.MouseUp,
                pending.Button,
                pending.X,
                pending.Y));
        }

        return actions;
    }

    private List<ScriptAction> BuildKeyboardActions(long stopTimestamp)
    {
        var actions = new List<ScriptAction>();
        var activeKeyStates = new Dictionary<Key, ActiveKeyState>();
        var modifierTracks = new List<ModifierTrack>();
        var keySpans = new List<KeySpan>();

        foreach (var keyboardEvent in _events.OfType<RawKeyboardInputEvent>())
        {
            if (keyboardEvent.IsKeyDown)
            {
                if (activeKeyStates.ContainsKey(keyboardEvent.Key))
                {
                    continue;
                }

                ModifierTrack? modifierTrack = null;
                if (IsModifierKey(keyboardEvent.Key))
                {
                    modifierTrack = new ModifierTrack(keyboardEvent.Key, keyboardEvent.Timestamp, keyboardEvent.Sequence);
                    modifierTracks.Add(modifierTrack);
                }

                activeKeyStates[keyboardEvent.Key] = new ActiveKeyState(
                    keyboardEvent.Key,
                    keyboardEvent.Timestamp,
                    keyboardEvent.Sequence,
                    activeKeyStates.Values
                        .Where(static state => state.Track is not null)
                        .Select(static state => state.Track!)
                        .ToList(),
                    modifierTrack);
                continue;
            }

            if (!activeKeyStates.Remove(keyboardEvent.Key, out var activeState))
            {
                continue;
            }

            if (activeState.Track is not null)
            {
                activeState.Track.UpTimestamp = keyboardEvent.Timestamp;
                activeState.Track.UpSequence = keyboardEvent.Sequence;
                continue;
            }

            var span = new KeySpan(
                activeState.Key,
                activeState.DownTimestamp,
                keyboardEvent.Timestamp,
                activeState.DownSequence,
                keyboardEvent.Sequence,
                activeState.ActiveModifiers);

            keySpans.Add(span);
            foreach (var modifier in activeState.ActiveModifiers)
            {
                modifier.UsedCount++;
                if (span.Duration > TapThresholdMilliseconds)
                {
                    modifier.MustEmitExplicit = true;
                }
            }
        }

        foreach (var remainingState in activeKeyStates.Values)
        {
            if (remainingState.Track is not null)
            {
                remainingState.Track.UpTimestamp = stopTimestamp;
                remainingState.Track.UpSequence = _nextSequence * 10;
                continue;
            }

            var span = new KeySpan(
                remainingState.Key,
                remainingState.DownTimestamp,
                stopTimestamp,
                remainingState.DownSequence,
                _nextSequence * 10,
                remainingState.ActiveModifiers);

            keySpans.Add(span);
            foreach (var modifier in remainingState.ActiveModifiers)
            {
                modifier.UsedCount++;
                modifier.MustEmitExplicit = true;
            }
        }

        foreach (var modifierTrack in modifierTracks)
        {
            var keyName = ToScriptKeyName(modifierTrack.Key);
            if (string.IsNullOrEmpty(keyName))
            {
                continue;
            }

            if (modifierTrack.MustEmitExplicit || modifierTrack.UsedCount == 0)
            {
                var duration = modifierTrack.UpTimestamp - modifierTrack.DownTimestamp;
                if (!modifierTrack.MustEmitExplicit
                    && modifierTrack.UsedCount == 0
                    && duration <= TapThresholdMilliseconds)
                {
                    actions.Add(new ScriptAction(
                        modifierTrack.DownTimestamp,
                        modifierTrack.DownSequence * 10,
                        ScriptActionType.KeyboardPress,
                        keyName));
                    continue;
                }

                actions.Add(new ScriptAction(
                    modifierTrack.DownTimestamp,
                    modifierTrack.DownSequence * 10,
                    ScriptActionType.KeyboardDown,
                    keyName));
                actions.Add(new ScriptAction(
                    modifierTrack.UpTimestamp,
                    modifierTrack.UpSequence * 10 + 1,
                    ScriptActionType.KeyboardUp,
                    keyName));
            }
        }

        foreach (var keySpan in keySpans)
        {
            var keyName = ToScriptKeyName(keySpan.Key);
            if (string.IsNullOrEmpty(keyName))
            {
                continue;
            }

            var hasExplicitModifier = keySpan.ActiveModifiers.Any(static modifier => modifier.MustEmitExplicit);
            if (hasExplicitModifier)
            {
                AddSimpleKeyActions(actions, keySpan, keyName);
                continue;
            }

            if (keySpan.ActiveModifiers.Count > 0 && keySpan.Duration <= TapThresholdMilliseconds)
            {
                actions.Add(new ScriptAction(
                    keySpan.DownTimestamp,
                    keySpan.DownSequence * 10,
                    ScriptActionType.KeyboardPress,
                    BuildCombinationExpression(keySpan.ActiveModifiers, keyName)));
                continue;
            }

            AddSimpleKeyActions(actions, keySpan, keyName);
        }

        return actions;
    }

    private static void AddSimpleKeyActions(List<ScriptAction> actions, KeySpan keySpan, string keyName)
    {
        if (keySpan.Duration <= TapThresholdMilliseconds)
        {
            actions.Add(new ScriptAction(
                keySpan.DownTimestamp,
                keySpan.DownSequence * 10,
                ScriptActionType.KeyboardPress,
                keyName));
            return;
        }

        actions.Add(new ScriptAction(
            keySpan.DownTimestamp,
            keySpan.DownSequence * 10,
            ScriptActionType.KeyboardDown,
            keyName));
        actions.Add(new ScriptAction(
            keySpan.UpTimestamp,
            keySpan.UpSequence * 10 + 1,
            ScriptActionType.KeyboardUp,
            keyName));
    }

    private static string RenderScript(string scriptName, string description, IReadOnlyList<ScriptAction> actions)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"-- @name: {scriptName}");
        builder.AppendLine($"-- @description: {description}");
        builder.AppendLine();

        if (actions.Count == 0)
        {
            builder.AppendLine("-- 本次录制未捕获到可回放事件。");
            return builder.ToString();
        }

        var lastTimestamp = 0L;
        foreach (var action in actions)
        {
            var delay = action.Timestamp - lastTimestamp;
            if (delay > 0)
            {
                builder.AppendLine($"host.sleep({delay})");
            }

            switch (action.Type)
            {
                case ScriptActionType.MouseMove:
                    builder.AppendLine($"mouse.move({action.X}, {action.Y})");
                    break;
                case ScriptActionType.MouseClick:
                    builder.AppendLine($"mouse.move({action.X}, {action.Y})");
                    builder.AppendLine($"mouse.click(\"{action.Value}\")");
                    break;
                case ScriptActionType.MouseDown:
                    builder.AppendLine($"mouse.move({action.X}, {action.Y})");
                    builder.AppendLine($"mouse.down(\"{action.Value}\")");
                    break;
                case ScriptActionType.MouseUp:
                    builder.AppendLine($"mouse.move({action.X}, {action.Y})");
                    builder.AppendLine($"mouse.up(\"{action.Value}\")");
                    break;
                case ScriptActionType.KeyboardPress:
                    builder.AppendLine($"keyboard.press(\"{action.Value}\")");
                    break;
                case ScriptActionType.KeyboardDown:
                    builder.AppendLine($"keyboard.down(\"{action.Value}\")");
                    break;
                case ScriptActionType.KeyboardUp:
                    builder.AppendLine($"keyboard.up(\"{action.Value}\")");
                    break;
            }

            lastTimestamp = action.Timestamp;
        }

        return builder.ToString();
    }

    private static string BuildCombinationExpression(IEnumerable<ModifierTrack> modifiers, string keyName)
    {
        var parts = modifiers
            .Select(static modifier => ToGenericModifierName(modifier.Key))
            .Where(static name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetModifierSortOrder)
            .ToList();
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private static int GetModifierSortOrder(string modifier)
    {
        return modifier switch
        {
            "Ctrl" => 0,
            "Alt" => 1,
            "Shift" => 2,
            "Win" => 3,
            _ => 10,
        };
    }

    private static bool IsSupportedMouseButton(string button)
    {
        return button.Equals("left", StringComparison.OrdinalIgnoreCase)
               || button.Equals("right", StringComparison.OrdinalIgnoreCase)
               || button.Equals("middle", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMouseButton(string button)
    {
        return button.Trim().ToLowerInvariant();
    }

    private static Key NormalizeKey(Key key)
    {
        return key == Key.System ? Key.None : key;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;
    }

    private static string ToGenericModifierName(Key key)
    {
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "Ctrl",
            Key.LeftAlt or Key.RightAlt => "Alt",
            Key.LeftShift or Key.RightShift => "Shift",
            Key.LWin or Key.RWin => "Win",
            _ => string.Empty,
        };
    }

    private static string ToScriptKeyName(Key key)
    {
        return key switch
        {
            Key.None => string.Empty,
            Key.LeftCtrl or Key.RightCtrl => "Ctrl",
            Key.LeftAlt or Key.RightAlt => "Alt",
            Key.LeftShift or Key.RightShift => "Shift",
            Key.LWin or Key.RWin => "Win",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Delete => "Del",
            Key.Insert => "Ins",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            Key.Space => "Space",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            _ => key.ToString(),
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    public readonly record struct CursorReading(int X, int Y, string ColorHex)
    {
        public string ToLogMessage()
        {
            return $"鼠标位置 ({X}, {Y}), 颜色 {ColorHex}";
        }
    }

    private abstract record RawInputEvent(long Timestamp, long Sequence);

    private sealed record RawMouseInputEvent(
        long Timestamp,
        long Sequence,
        string Button,
        int X,
        int Y,
        bool IsButtonDown) : RawInputEvent(Timestamp, Sequence);

    private sealed record RawKeyboardInputEvent(
        long Timestamp,
        long Sequence,
        Key Key,
        bool IsKeyDown) : RawInputEvent(Timestamp, Sequence);

    private sealed class ActiveKeyState
    {
        public ActiveKeyState(
            Key key,
            long downTimestamp,
            long downSequence,
            List<ModifierTrack> activeModifiers,
            ModifierTrack? track)
        {
            Key = key;
            DownTimestamp = downTimestamp;
            DownSequence = downSequence;
            ActiveModifiers = activeModifiers;
            Track = track;
        }

        public Key Key { get; }

        public long DownTimestamp { get; }

        public long DownSequence { get; }

        public List<ModifierTrack> ActiveModifiers { get; }

        public ModifierTrack? Track { get; }
    }

    private sealed class ModifierTrack
    {
        public ModifierTrack(Key key, long downTimestamp, long downSequence)
        {
            Key = key;
            DownTimestamp = downTimestamp;
            DownSequence = downSequence;
            UpTimestamp = downTimestamp;
            UpSequence = downSequence;
        }

        public Key Key { get; }

        public long DownTimestamp { get; }

        public long DownSequence { get; }

        public long UpTimestamp { get; set; }

        public long UpSequence { get; set; }

        public int UsedCount { get; set; }

        public bool MustEmitExplicit { get; set; }
    }

    private sealed class KeySpan
    {
        public KeySpan(
            Key key,
            long downTimestamp,
            long upTimestamp,
            long downSequence,
            long upSequence,
            List<ModifierTrack> activeModifiers)
        {
            Key = key;
            DownTimestamp = downTimestamp;
            UpTimestamp = upTimestamp;
            DownSequence = downSequence;
            UpSequence = upSequence;
            ActiveModifiers = activeModifiers;
        }

        public Key Key { get; }

        public long DownTimestamp { get; }

        public long UpTimestamp { get; }

        public long DownSequence { get; }

        public long UpSequence { get; }

        public long Duration => UpTimestamp - DownTimestamp;

        public List<ModifierTrack> ActiveModifiers { get; }
    }

    private sealed record ScriptAction(
        long Timestamp,
        long OrderKey,
        ScriptActionType Type,
        string? Value = null,
        int X = 0,
        int Y = 0);

    private enum ScriptActionType
    {
        MouseMove,
        MouseClick,
        MouseDown,
        MouseUp,
        KeyboardPress,
        KeyboardDown,
        KeyboardUp,
    }
}
