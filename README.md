# Plux Adapter

Connects to sensors and distributes their data over TCP/IP allowing connection to incompatible devices, data distribution to multiple clients and aggregated csv logging.

## Installation

For development:
1. [https://dotnet.microsoft.com/download/dotnet/5.0](https://dotnet.microsoft.com/download/dotnet/5.0) - used to compile code.
1. [https://dotnet.microsoft.com/download/dotnet-framework/net451](https://dotnet.microsoft.com/download/dotnet-framework/net451) - used to compile code.
1. [https://github.com/biosignalsplux/c-sharp-sample](https://github.com/biosignalsplux/c-sharp-sample) - used to connect to Plux devices, simply drop "plux.dll" and (optional - used for intellisense) "plux.xml" in [PluxAdapter/Servers/lib/PluxDotNet](./PluxAdapter/Servers/lib/PluxDotNet).
1. (optional) [https://code.visualstudio.com/](https://code.visualstudio.com/) - used to write code. Note that there are some VSC specific configuration files included in [.vscode](./.vscode). Especially consider [.vscode/launch.json](./.vscode/launch.json), without it VSC can't execute code(note that "dotnet.exe" still can, it's just that VSC can't figure out if it's supposed to use ".NET Framework" or ".NET Core" debugger).

For deployment:
1. Compile source(see [Compilation](#Compilation)).
1. Distribute client or server depending on use case, but note that:
    * Server can also perform as client.
    * Target machine requires "Runtime" from [https://dotnet.microsoft.com/download/dotnet-framework/net451](https://dotnet.microsoft.com/download/dotnet-framework/net451) or later("Windows 8.1" or later has it by default).

## Compilation

To compile from source:
1. Make sure "Developer Pack" from [https://dotnet.microsoft.com/download/dotnet-framework/net451](https://dotnet.microsoft.com/download/dotnet-framework/net451) is installed.
1. Make sure "SDK" from [https://dotnet.microsoft.com/download/dotnet/5.0](https://dotnet.microsoft.com/download/dotnet/5.0) is installed.
1. Download/clone the source from [https://github.com/arehab-app/plux-adapter](https://github.com/arehab-app/plux-adapter) and extract it somewhere.
1. Download/clone plux driver from [https://github.com/biosignalsplux/c-sharp-sample](https://github.com/biosignalsplux/c-sharp-sample) and extract it next to "plux-adapter" directory.
1. Go to "plux-adapter/PluxAdapter/Clients" directory and execute "dotnet.exe publish" from terminal:
    ```bash
    cd plux-adapter/PluxAdapter/Clients
    dotnet.exe publish
    ```
1. Go to "plux-adapter/PluxAdapter/Servers" directory and copy/symlink "plux-adapter/PluxAdapter/Clients/bin/Debug/net451/publish" directory to "plux-adapter/PluxAdapter/Servers/lib/PluxAdapter" directory:
    ```bash
    cd ../Servers
    mkdir lib
    cp -r ../Clients/bin/Debug/net451/publish lib/PluxAdapter
    ```
1. While still in "plux-adapter/PluxAdapter/Servers" directory copy/symlink "c-sharp-sample/64-bit" directory to "plux-adapter/PluxAdapter/Servers/lib/PluxDotNet" directory:
    ```bash
    cp -r ../../../c-sharp-sample/64-bit lib/PluxDotNet
    ```
1. While still in "plux-adapter/PluxAdapter/Servers" directory  execute "dotnet.exe publish" from terminal:
    ```bash
    dotnet.exe publish
    ```
1. In "plux-adapter/PluxAdapter/Clients/bin/Debug/net451/publish" directory is everything that's required for client, mainly it contains dll for programmatic access and xml for intellisense.
1. In "plux-adapter/PluxAdapter/Servers/bin/Debug/net451/publish" directory is everything that's required for server, mainly it contains everything from "plux-adapter/PluxAdapter/Clients/bin/Debug/net451/publish" directory, exe for execution/programmatic access, xml for intellisense and nlog for logging configuration.

## Examples

From command line:
* Execute server.
    ```bash
    ./PluxAdapter.Servers.exe
    ```
* Execute client.
    ```bash
    ./PluxAdapter.Servers.exe client
    ```
* Get general help.
    ```bash
    ./PluxAdapter.Servers.exe --help
    ```
* Get server specific help.
    ```bash
    ./PluxAdapter.Servers.exe server --help
    ```

From code:
* Connect to local server from unity and request specific devices(note that server must be running in parallel, see [Examples](#Examples)):
    ```c#
    using System;
    using System.Threading.Tasks;
    using System.Collections.Generic;

    using UnityEngine;

    using PluxAdapter.Clients;

    public class PluxBehaviour : MonoBehaviour
    {
        private Client.FrameReceivedEventArgs eventArgs;

        private void Start()
        {
            // create new client with default ip and port requesting specific devices
            Client client = new Client(new Client.Options("127.0.0.1", 24242, new List<string> { "BTH00:07:80:46:F0:31", "BTH00:07:80:4D:2D:77" }));
            // update eventArgs with the newest ones
            client.FrameReceived += (sender, eventArgs) => this.eventArgs = eventArgs;
            // start communication
            Task.Run(client.Start);
        }

        private void Update()
        {
            // log the newest received frame counter and data
            if (!(eventArgs is null)) { Debug.Log($"{eventArgs.currentFrame} - {String.Join(", ", eventArgs.data)}"); }
        }
    }
    ```

## Architecture

Plux Adapter is library with command line interface, it has two modes of operation:
* As server it can connect to sensors and distribute their data to clients.
* As client it can connect to server and receive data from sensors.

![Plux Adapter architecture](./Documentation/Images/Architecture.png)

## Structure

Plux Adapter is structured in the following main classes(note that many other classes are employed, these are just the central ones):
* Program - the main entry point from command line.
* Server - listens for connections from Clients and manages Handlers.
* Handler - negotiates with Client and transfers raw data from Devices.
* Device - manages connection to physical sensor.
* Manager - manages and searches for Devices.
* Client - connects to Server and receives raw data from Handler.

![Plux Adapter structure](./Documentation/Images/Structure.png)

## Logging

Two types of logs are generated(all directories are relative to the main application executable):
* Control logs are located in "logs" directory, these contain general purpose logs of everything of note that's going on in the library.
    * These are configured in [PluxAdapter/Servers/PluxAdapter.Servers.exe.nlog](./PluxAdapter/Servers/PluxAdapter.Servers.exe.nlog).
* Data logs are located in "data" directory, these contain raw data received from sensors.
    * The files are stamped with logging start time and sensor path.
    * The files contain csv data with:
        * Frame counter since device start.
        * Time of frame as number of ticks(each tick is 100 ns, see [https://docs.microsoft.com/en-us/dotnet/api/system.datetime.ticks?view=netframework-4.5.1#remarks](https://docs.microsoft.com/en-us/dotnet/api/system.datetime.ticks?view=netframework-4.5.1#remarks)) since the epoch(1970-01-01, see [https://en.wikipedia.org/wiki/Epoch_(computing)](https://en.wikipedia.org/wiki/Epoch_(computing))).
        * Data itself is stored in following columns using {sensor port}-{port channel} convention for column names(example: "11-0,11-1" are 2 columns for 11th port with 0th and 1st channel).

## License

For licensing information see [LICENSE](./LICENSE.md).
