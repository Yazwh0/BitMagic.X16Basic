using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Basic.DapTests;

// X16D's getScreen request, see BitMagic.X16Debugger/CustomMessage/LayerView.cs
internal sealed class ScreenRequest : DebugRequestWithResponse<ScreenRequestArguments, ScreenRequestResponse>
{
    public ScreenRequest(string? renderMode) : base("getScreen")
    {
        Args.RenderMode = renderMode;
    }
}

internal sealed class ScreenRequestArguments : DebugRequestArguments
{
    public string? RenderMode { get; set; }
}

internal sealed class ScreenRequestResponse : ResponseBody
{
    public string Display { get; set; } = "";
}

// X16D's keyboardInput request, see BitMagic.X16Debugger/CustomMessage/KeyboardInput.cs
internal sealed class KeyboardRequest : DebugRequestWithResponse<KeyboardRequestArguments, KeyboardRequestResponse>
{
    public KeyboardRequest(string key, bool down) : base("keyboardInput")
    {
        Args.Key = key;
        Args.Down = down;
    }
}

internal sealed class KeyboardRequestArguments : DebugRequestArguments
{
    public string Key { get; set; } = "";
    public bool Down { get; set; }
}

internal sealed class KeyboardRequestResponse : ResponseBody
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
