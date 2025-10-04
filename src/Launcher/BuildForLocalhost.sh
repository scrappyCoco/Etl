#!/bin/bash

# The name of the local NuGet repository.
# Get it using the command:
# > dotnet nuget list source
source=LocalNuGetRepository
config=Release

echo "Building..."
dotnet clean && dotnet build --configuration $config --runtime win-x64

echo "Removing old packages from the NuGet cache..."
rm -rf $(dotnet nuget locals global-packages --list)coding4fun.etl.launcher

# We don't know a version of the package yet.
nukpgPath=$(find ./bin/$config -type f -iname "Coding4Fun.Etl.Launcher.*.nupkg")
echo "Pushing $nukpgPath"
dotnet nuget push $nukpgPath --source $source