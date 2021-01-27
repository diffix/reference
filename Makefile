start:
	dotnet watch -p src/OpenDiffix.Web/ run

update:
	git checkout master && git pull

release-macos:
	dotnet publish -r osx-x64 -o build/ -c Release /p:PublishSingleFile=true /p:PublishTrimmed=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true src/OpenDiffix.CLI/

new-macos: update release-macos

release-win:
	dotnet publish -r win-x64 -o build/ -c Release /p:PublishSingleFile=true /p:PublishTrimmed=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true src/OpenDiffix.CLI/

new-win: update release-win

release-linux:
	dotnet publish -r linux-x64 -o build/ -c Release /p:PublishSingleFile=true /p:PublishTrimmed=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true src/OpenDiffix.CLI/

new-linus: update release-linux
