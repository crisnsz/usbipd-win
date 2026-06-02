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
    public const string PipeGroupName = "usbipd_users";
    public const int ConnectTimeoutMs = 5_000;
}
