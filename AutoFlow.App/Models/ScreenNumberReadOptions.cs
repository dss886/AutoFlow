namespace AutoFlow.App.Models;

public enum ScreenNumberReadMode
{
    Integer,
    Float,
}

public sealed class ScreenNumberReadOptions
{
    public string Language { get; init; } = "eng";

    public string CharacterWhitelist { get; init; } = "0123456789";

    public int Scale { get; init; } = 3;

    public byte? Threshold { get; init; }

    public bool Invert { get; init; }

    public bool TrimResult { get; init; } = true;

    public ScreenNumberReadMode Mode { get; init; } = ScreenNumberReadMode.Integer;

    public int MaxCandidates { get; init; } = 3;
}
