start:
	ASPNETCORE_ENVIRONMENT=development dotnet watch -p src/Website.Server/ run

release-macos:
	dotnet publish -r osx-x64 -o build -c Release /p:PublishSingleFile=true /p:PublishTrimmed=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true src/OpenDiffix.CLI/

release-linux:
	dotnet publish -r linux-x64 -o build -c Release /p:PublishSingleFile=true /p:PublishTrimmed=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true src/OpenDiffix.CLI/
