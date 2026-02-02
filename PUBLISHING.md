# STING List Manager - Windows App Publishing

## Quick Start for Users

Your app is ready to use! Just run:

```
dotnet run
```

## Package for Distribution

To create a standalone Windows executable that users can run without .NET installed:

### Option 1: Run the publish script (easy)
```
.\publish.bat
```

### Option 2: Manual publish command
```
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false
```

The result will be at: `.\bin\Release\net8.0\win-x64\publish\StingListManager.exe`

## Distribution

1. After publish, the entire `publish` folder is your distribution
2. Copy `StingListManager.exe` (and the folder if needed)
3. Users can run it directly - no .NET installation required
4. Create a shortcut: Right-click `StingListManager.exe` → Send to → Desktop (create shortcut)

## System Requirements for Users

- Windows 10 or later (x64)
- That's it! Everything is bundled inside the EXE

## File Size

- Standalone EXE: ~120-150 MB (includes all .NET runtime + SQLite)
- Much smaller if distributed as an installer (future step)

---

## Development

Run in debug mode:
```
dotnet run
```

Build for testing:
```
dotnet build
```

Create migrations (for database changes):
```
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

---

Enjoy your STING List Manager! 🚀
