# To build

1. Source init.ps1
    - In powershell, '`. .\init.ps1`'
2. Build by running '`dotnet build -c Release`'.
    - There is also a `launch.json` and `tasks.json` for running in VSCode, but you will need to update the path references as I couldn't get it to work with environment variables.
3. Output is under `Release\VsTrashcan.zip`.

# To use

1. Copy `VsTrashcan.zip` into `Mods` folder under `VintageStory`.

# Copyright Info

This mod is only possible because of the following projects:

- https://github.com/copygirl/howto-example-mod
    - Published under public domain

- https://github.com/p3t3rix-vsmods/VsProspectorInfo
    - Published under MIT.