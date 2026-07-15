# Kelly Utils

Kelly Utils is a mod for Jundroo's [Juno: New Origins](https://simplerockets.com) (JNO) that fixes my pet peeves with the game.

## Features

- Fix the floating point precision problem causing Drood FPV and camera PoVs to start shaking over time as they become further from the Unity game scene's origin.
- Fix first-person camera clip planes so [Parallax](https://www.simplerockets.com/Mods/View/240051/Parallax) terrain scatter appears further.

## License

This project’s source code is licensed under the [MIT License](LICENSE).

> [!important]
> 
> This software is provided "as is", without warranty of any kind.
>
> This license applies only to the original source code written for Kelly Utils. Unity and Juno: New Origins remain proprietary software, and their engine libraries, assemblies, and any automatically generated skeleton code are not covered by this license. Any third-party plugins and libraries included and referenced are distributed under their original licenses, which are provided in their respective source and documentation.
>
> To build or use this mod, you must have a licensed copy of Unity, Juno: New Origins, and any applicable plugins.

## Installation

Download the newest [release](https://github.com/KellyNyanbinary/kelly-juno-utils/releases) and put it into `%USERPROFILE%\AppData\LocalLow\Jundroo\SimpleRockets 2\Mods` on Windows and `~/Library/Application Support/com.jundroo.SimpleRockets2/Mods` on macOS.

## Development

1. Follow the "Downloading Unity" section of the [JNO modding guide](https://www.simplerockets.com/Forums/View/31506/).
    1. Install Unity 2022.3.62f3 with macOS and Windows build components for mono.
    2. Install the C# IDE of your choice. (Unity defaults to Visual Studio; I use Rider.)
2. [Clone](https://docs.github.com/en/repositories/creating-and-managing-repositories/cloning-a-repository) the Kelly Utils repository.
3. Open the repository as a Unity Project.
4. Follow the JNO modding guide to import the JNO mod tools.
    1. In Unity, Assets → Import Package → Custom Package, and import the mod tools at `JNO installation directory/ModTools/SimpleRockets2_ModTools.unitypackage`. The default location for this on Windows is `C:\Program Files (x86)\Steam\steamapps\common\SimpleRockets2\ModTools\SimpleRockets2_ModTools.unitypackage`. The default location for this on macOS is `~/Library/Application Support/Steam/steamapps/common/SimpleRockets2/ModTools/SimpleRockets2_ModTools.unitypackage`.
    2. Initialize the mod in Unity via SimpleRockets 2 → Mod Builder Window → Start Creating Mod.
5. Install the Harmony Unity plugin.
    1. Download the newest "Fat" version of the [Harmony plugin](https://github.com/pardeike/Harmony/releases).
    2. Copy the file `net472/0Harmony.dll` into `Assets/Plugins/Harmony`.
    3. Optionally, you may also include `net472/0Harmony.xml` in `Assets/Plugins/Harmony`.
    4. Return to the Unity Editor, and it should automatically begin importing the plugin.
6. In Unity, select `Assets/Kelly Utils.asmdef`. In the Inspector, enable "Override References" and add `0Harmony.dll` to "Assembly References". Click "Apply".
7. (Optional) Decompile the game assemblies for reference. Redo this every time the game updates.
    1. Make sure .NET 10 SDK is installed. Install it via one of the following methods:
        - The [official .NET website](https://dotnet.microsoft.com/en-us/download/dotnet),
        - Winget on Windows via `winget install Microsoft.DotNet.SDK.10`,
        - or [Homebrew](https://docs.brew.sh/Installation) on macOS via `brew install dotnet`.
    2. This project uses the `ilspycmd` command-line tool for decompilation. On both Windows and macOS:
        ```
        dotnet tool install --global ilspycmd
        ```
    3. In your shell (terminal), set `ASSEMBLY_PATH` to the path to the game assemblies:

        On Windows PowerShell:
        ```PowerShell
        $env:ASSEMBLY_PATH = "C:\Program Files (x86)\Steam\steamapps\common\SimpleRockets2\SimpleRockets2_Data\Managed"
        ```

        On macOS Zsh:
        ```zsh
        export ASSEMBLY_PATH="$HOME/Library/Application Support/Steam/steamapps/common/SimpleRockets2/SimpleRockets2.app/Contents/Resources/Data/Managed"
        ```
        Update this path if the game is installed elsewhere.
    4. From the repository root, run the commands for your operating system to decompile each assembly into a separate folder under `Decompiled`:

        On Windows PowerShell:
        ```PowerShell
        ilspycmd -p -o Decompiled\SimpleRockets2        "$env:ASSEMBLY_PATH\SimpleRockets2.dll"
        ilspycmd -p -o Decompiled\ModApi                "$env:ASSEMBLY_PATH\ModApi.dll"
        ilspycmd -p -o Decompiled\ModApi.Core           "$env:ASSEMBLY_PATH\ModApi.Core.dll"
        ilspycmd -p -o Decompiled\Jundroo.ModTools      "$env:ASSEMBLY_PATH\Jundroo.ModTools.dll"
        ilspycmd -p -o Decompiled\Jundroo.ModTools.Core "$env:ASSEMBLY_PATH\Jundroo.ModTools.Core.dll"
        ilspycmd -p -o Decompiled\XmlLayout             "$env:ASSEMBLY_PATH\XmlLayout.dll"
        ```

        On macOS Zsh:
        ```zsh
        ilspycmd -p -o Decompiled/SimpleRockets2        "${ASSEMBLY_PATH}/SimpleRockets2.dll"
        ilspycmd -p -o Decompiled/ModApi                "${ASSEMBLY_PATH}/ModApi.dll"
        ilspycmd -p -o Decompiled/ModApi.Core           "${ASSEMBLY_PATH}/ModApi.Core.dll"
        ilspycmd -p -o Decompiled/Jundroo.ModTools      "${ASSEMBLY_PATH}/Jundroo.ModTools.dll"
        ilspycmd -p -o Decompiled/Jundroo.ModTools.Core "${ASSEMBLY_PATH}/Jundroo.ModTools.Core.dll"
        ilspycmd -p -o Decompiled/XmlLayout             "${ASSEMBLY_PATH}/XmlLayout.dll"
        ```
