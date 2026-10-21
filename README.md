# Shotto

A small screenshot tool for Windows 11, in the spirit of the Snipping Tool: press **Print Screen**, drag over what you want, and it's on your clipboard.

![Shotto icon](Assets/Shotto.ico)

## Features

- **Capture modes** – drag to capture an area (or click to grab the whole screen), capture the monitor under the pointer, or all monitors at once.
- **Clipboard first** – every capture is copied straight away (as both a bitmap and PNG, so it pastes into chat apps, browsers and Office).
- **Preview** – a thumbnail pops up in the corner with Edit, Save and Save as.
- **Editor** – pen, highlighter, rectangle, ellipse and arrow (hold Shift for squares and circles), a color picker with palette, hue/saturation picker and hex entry, sizes from 1 to 48 px, undo/redo, zoom. Copy or save the marked-up image.
- **Rebindable shortcuts** – any key with Ctrl/Alt/Shift/Win, or Print Screen, Pause, Scroll Lock and F-keys on their own.
- **Saving** – Save goes to your chosen folder (Downloads by default) as PNG or JPG; Save as lets you pick.
- **Starts with Windows** – optionally, and silently into the notification area.
- **Owns Print Screen** – can turn off Windows' "Use the Print screen key to open screen capture" so Snipping Tool doesn't open even when Shotto is closed. Turning the option off restores the Windows setting.
- Windows 11 look (Fluent theme, Mica, light/dark and accent color), per-monitor DPI aware.

## Default shortcuts

| Action | Shortcut |
|---|---|
| Capture an area | Print Screen |
| Capture this screen | Shift + Print Screen |
| Capture all screens | Ctrl + Print Screen |

In the editor: `P` pen, `H` highlighter, `R` rectangle, `E` ellipse, `A` arrow, `V` move; `Ctrl+Z`/`Ctrl+Y` undo/redo, `Ctrl+C` copy, `Ctrl+S` save, `Ctrl+Shift+S` save as, `Ctrl+0` fit, `Ctrl+1` actual size, `Ctrl`+wheel zoom.

## Build

Requires Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build -c Release
```

The app is `bin/Release/net10.0-windows/Shotto.exe` and needs the .NET 10 Desktop Runtime. Run it with `--silent` to start in the notification area.

Settings are stored in `%APPDATA%\Shotto\settings.json`.

`tools/make-icon.ps1` regenerates `Assets/Shotto.ico`.

## License

[MIT](LICENSE)
