dotnet publish -r win-x64 -o build -c Release /p:PublishSingleFile=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true src/OpenDiffix.CLI/
