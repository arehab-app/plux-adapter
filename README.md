# Plux Adapter

Connects to sensors and distributes their data over TCP/IP allowing connection to incompatible devices, data distribution to multiple clients and aggregated csv logging.

## Installation

For development:
* [https://github.com/biosignalsplux/c-sharp-sample](https://github.com/biosignalsplux/c-sharp-sample) - used to connect to Plux devices, simply drop "plux.dll" and (optional - used for intellisense) "plux.xml" in [PluxAdapter/lib/PluxDotNet](./PluxAdapter/lib/PluxDotNet).
* [https://dotnet.microsoft.com/download/dotnet/5.0](https://dotnet.microsoft.com/download/dotnet/5.0) - used to compile code.
* (optional) [https://dotnet.microsoft.com/download/dotnet-framework/net451](https://dotnet.microsoft.com/download/dotnet-framework/net451) - used for intellisense.
* (optional) [https://code.visualstudio.com/](https://code.visualstudio.com/) - used to write code. Note that there are some VSC specific configuration files included in [.vscode](./.vscode). Especially consider [.vscode/launch.json](./.vscode/launch.json), without it VSC can't execute code(note that "dotnet.exe" still can, it's just that VSC can't figure out if it's supposed to use ".NET Framework" or ".NET Core" debugger).

For deployment:
* Just drop [https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) result somewhere and execute it(note that [https://dotnet.microsoft.com/download/dotnet-framework/net451](https://dotnet.microsoft.com/download/dotnet-framework/net451) is required for execution, it's shipped with "Windows 8.1" or later).

## Examples

* Execute server.
    ```bash
    ./PluxAdapter.exe
    ```
* Execute client.
    ```bash
    ./PluxAdapter.exe client
    ```
* Get general help.
    ```bash
    ./PluxAdapter.exe --help
    ```
* Get server specific help.
    ```bash
    ./PluxAdapter.exe server --help
    ```

## Architecture

Plux Adapter is library with command line interface, it has two modes of operation:
* As server it can connect to sensors and distribute their data to clients.
* As client it can connect to server and receive data from sensors.

![Plux Adapter architecture](./PluxAdapter/doc/images/Architecture.png)

## Structure

Plux Adapter is structured in following main classes(note that many other classes are employed, these are just central ones):
* Program - main entry point from command line.
* Server - listens for connections from Clients and manages Handlers.
* Handler - negotiates with Client and transfers raw data from Devices.
* Device - manages connection to physical sensor.
* Manager - manages and searches for Devices.
* Client - connects to Server and receives raw data from Handler.

![Plux Adapter structure](./PluxAdapter/doc/images/Structure.png)

## Logging

Two types of logs are generated(all directories are relative to application executable):
* Control logs are located in "logs" directory, these contain general purpose logs of everything of note that's going on in library. These are configured in [PluxAdapter.exe.nlog](./PluxAdapter/PluxAdapter.exe.nlog).
* Data logs are located in "data" directory, these contain raw data received from sensors. These are stamped with start time and sensor path.

## License

For licensing information see [LICENSE](./LICENSE.md).
