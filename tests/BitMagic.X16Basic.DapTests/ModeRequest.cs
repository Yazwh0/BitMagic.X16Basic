using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Basic.DapTests;

// X16D's x16Mode request, see BitMagic.X16Debugger/CustomMessage/ModeRequest.cs
internal sealed class ModeRequest : DebugRequestWithResponse<ModeRequestArguments, ModeRequestResponse>
{
    public ModeRequest(string command) : base("x16Mode")
    {
        Args.Command = command;
    }
}

internal sealed class ModeRequestArguments : DebugRequestArguments
{
    public string Command { get; set; } = "";
    public JObject? Arguments { get; set; }
}

internal sealed class ModeRequestResponse : ResponseBody
{
    public JToken? Result { get; set; }
}
