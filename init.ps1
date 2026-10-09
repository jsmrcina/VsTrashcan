# Points the build at the game install. On Linux, the AUR package installs to /opt/vintagestory.
if ($IsWindows -or $env:OS -eq "Windows_NT") {
    $env:VINTAGE_STORY = "$env:APPDATA\vintagestory"
    $env:VINTAGE_STORY_DATA = "$env:APPDATA\VintagestoryData"
} else {
    $env:VINTAGE_STORY = "/opt/vintagestory"
    $env:VINTAGE_STORY_DATA = "$HOME/.config/VintagestoryData"
}
