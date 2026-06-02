namespace Usbipd;

enum BindPipeCommand : byte
{
    Bind = 1,
    Unbind = 2,
    UnbindAll = 3,
}

enum BindPipeMessageLevel : byte
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

readonly record struct BindPipeMessage(BindPipeMessageLevel Level, string Text);

static class BindPipeProtocol
{
    public const string PipeName = "usbipd-bind";
    public const string PipeGroupName = "usbipd-users";
    public const int ConnectTimeoutMs = 5_000;

    // Maximum allowed length (in UTF-8 bytes) of any single string read from a pipe client.
    // Bounds memory consumption from a malicious or buggy client. Real instance IDs and
    // descriptions are well under 1 KiB.
    public const int MaxStringByteLength = 4 * 1024;

    // Maximum time allowed to read a complete request from a connected client.
    // Drains slow / silent clients so they cannot tie up server pipe instances indefinitely.
    public const int RequestReadTimeoutMs = 10_000;

    // Maximum number of concurrent server pipe instances.
    public const int MaxServerInstances = 4;
}
