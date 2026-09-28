using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols.TelnetServer.Telnet;

public interface ITelnetApp
{
    Task OnConnectedAsync(TelnetSession session);
}
