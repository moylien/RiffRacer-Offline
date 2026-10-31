# Riff Racer Offline Patch (Plug & Play)

Restores custom track generation ("New Track" / "Crear Nueva Pista") and full offline functionality for **Riff Racer** (also known as *Drive Any Track*).

Following the official shutdown of the Parse.com backend servers, Riff Racer's main menu disables custom track creation because the game is unable to handshake with the backend. This patch replaces the remote Parse client with a lightweight, 100% local, self-contained offline implementation.

---

## Features
- **No external servers or Python required:** Everything runs locally and natively inside the game process.
- **Zero configuration / Plug & Play:** Uses real strong-name cryptographic assembly signing; no registry edits, administrative permissions, or developer tools needed.
- **Preserves existing save data:** Vehicle unlocks, stats, and profile progression remain completely intact.
- **Steam & Offline compatible:** Works with official Steam installations as well as offline configurations.

---

## Installation (For Players)

1. Navigate to your Riff Racer game directory (where `Game.exe` is located).
2. Copy the following 3 files from the `release/` folder of this repository into your game directory, overwriting when prompted:
   - `Parse.dll`
   - `Parse.NetFx45.dll`
   - `ParseOctane.dll`
3. Launch `Game.exe`. The option to create new tracks will now be active in the main menu.

All local data (offline player profile, track conversion cost configuration, and local track file data) is automatically stored in a `LocalData\` subfolder within the game directory.

---

## Technical Details

- **Parse Client Replacement (`Parse.dll`):** Clean-room implementation targeting .NET Framework 4.5 / C# 5 with zero third-party dependencies, mimicking Parse .NET SDK 1.7.0.0.
- **Storage:** Local JSON-based store with automatic seeding for `Configuration` (TrackConversionCost = 0), `Version` (Ver = 100), `Profile_Steam`, and `News`.
- **Local File Server:** Embedded HTTP loopback listener handling `ParseFile` upload and download requests locally for audio analysis data.
- **Signature & Token:** Cryptographically strong-name signed, paired with a matching public key token in the modified `ParseOctane.dll` wrapper.

---

## Building from Source

To compile the assemblies manually using the standard .NET Framework 4.8 C# compiler:

```cmd
build.cmd
```

Requirements:
- Windows with .NET Framework 4.0 or higher.
