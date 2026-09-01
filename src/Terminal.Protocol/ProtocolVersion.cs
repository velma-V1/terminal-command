namespace Terminal.Protocol;

public readonly record struct ProtocolVersion(ushort Major, ushort Minor)
{
    public static ProtocolVersion Current { get; } = new(1, 0);
}

public enum ProtocolMessageType : ushort
{
    Hello = 1,
    Health = 2,
    ActionPrepare = 3,
    ActionExecute = 4,
    Stdout = 5,
    Stderr = 6,
    Signal = 7,
    Cancel = 8,
    Verify = 9,
    Result = 10,
    Heartbeat = 11,
    SystemFact = 12,
    Error = 13
}
