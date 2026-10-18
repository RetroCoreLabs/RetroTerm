//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.TestServer.App;

namespace RetroTerm.TestServer;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("+-----------------------------------------------------------------+");
        Console.WriteLine("|                  RetroTerm Test Server                            |");
        Console.WriteLine("+-----------------------------------------------------------------+");
        Console.WriteLine();
        Console.WriteLine($"Version: {BuildInfo.Version}");
        Console.WriteLine($"Build:   {BuildInfo.BuildDateTimeString}");
        Console.WriteLine();
        Console.WriteLine("=================================================================");
        Console.WriteLine();

        int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 23;
        Console.WriteLine($"RetroTerm Test Server listening on port {port}");

        var app = new TestServerApp();
        var server = new TelnetServer(port, app);
        await server.RunAsync();
    }
}
