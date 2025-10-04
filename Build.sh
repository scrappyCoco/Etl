# Output all commands
set -o xtrace

dotnet tool restore
dotnet build ./src/Build/Build.csproj --configuration Release
semVer=`dotnet gitversion /showvariable SemVer`

# Building Launcher
dotnet build ./src/Launcher/Launcher.csproj --configuration Release --runtime win-x64
dotnet build ./src/Launcher/Launcher.csproj --configuration Release --runtime linux-x64
dotnet build ./src/Launcher/Launcher.csproj --configuration Release --runtime osx-x64
dotnet pack ./src/Launcher/Launcher.csproj --configuration Release

dotnet build ./test/TestData/StageDb/StageDb.sqlproj --configuration Release

sourceVersionPattern="1.0.0"
replacementPattern="${semVer}"
sedCommand="s/${sourceVersionPattern}/${replacementPattern}/g"
sed -i "${sedCommand}" "Directory.Packages.props"

dotnet restore ./test/TestData/CoreDb/CoreDb.sqlproj --configfile ./test/TestData/CoreDb/nuget.config
dotnet build ./test/TestData/CoreDb/CoreDb.sqlproj --configuration Release --no-restore
dotnet test ./test/BuildTest/BuildTest.csproj
dotnet test ./test/IntegrationTest/IntegrationTest.csproj

branchName=`git rev-parse --abbrev-ref HEAD`
tagWithVersion=`git tag --points-at HEAD | grep -E 'v\d+\.\d+\.\d+'`

echo "${semVer}"
if [ "$branchName" = "main" ] && [ -n "$tagWithVersion" ]
then
    dotnet nuget push ./src/Build/bin/Release/Coding4Fun.Etl.Build.$semVer.nupkg -k $NUGET_API_KEY -s https://api.nuget.org/v3/index.json
    dotnet nuget push ./src/Launcher/bin/Release/Coding4Fun.Etl.Launcher.$semVer.nupkg -k $NUGET_API_KEY -s https://api.nuget.org/v3/index.json
fi