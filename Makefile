start:
	ASPNETCORE_ENVIRONMENT=development dotnet watch -p src/Website.Server/ run

release-macos:
	dotnet publish -r osx-x64 -o build -c Release /p:PublishSingleFile=true /p:PublishTrimmed=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true src/OpenDiffix.CLI/

release-linux:
	dotnet publish -r linux-x64 -o build -c Release /p:PublishSingleFile=true /p:PublishTrimmed=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true src/OpenDiffix.CLI/

dev-css:
	yarn dev-css

prod-css:
	yarn prod-css

playground: prod-css
	dotnet publish -c Release -o release src/Website.Client
