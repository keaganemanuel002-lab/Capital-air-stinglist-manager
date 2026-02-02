@echo off
REM Publish STING List Manager as a self-contained Windows executable
REM This creates an EXE you can distribute to users

echo Building STING List Manager...
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false

echo.
echo Build complete! Your executable is ready at:
echo .\bin\Release\net8.0\win-x64\publish\StingListManager.exe
echo.
echo You can now:
echo 1. Copy the entire publish folder to distribute to users
echo 2. Create a shortcut to StingListManager.exe
echo 3. Users run it without needing .NET installed
pause
