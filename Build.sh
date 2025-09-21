dotnet build ./src/Build/Build.csproj --configuration Release
dotnet build ./src/EtlLauncher/EtlLauncher.csproj --configuration Release
dotnet build ./test/TestData/StageDb/StageDb.sqlproj --configuration Release
dotnet restore ./test/TestData/CoreDb/CoreDb.sqlproj --configfile ./test/TestData/CoreDb/nuget.config
dotnet build ./test/TestData/CoreDb/CoreDb.sqlproj --configuration Release --no-restore
dotnet test ./test/BuildTest/BuildTest.csproj
dotnet test ./test/IntegrationTest/IntegrationTest.csproj

branchName=`git rev-parse --abbrev-ref HEAD`

if [ "$branchName" = "main" ]
then
    apiKey=`printenv NUGET_API_KEY`
    echo "${semVer}"
    dotnet nuget push ./src/Build/Build/bin/Release/Coding4Fun.Sql.Build.$semVer.nupkg -k $apiKey -s https://api.nuget.org/v3/index.json
fi