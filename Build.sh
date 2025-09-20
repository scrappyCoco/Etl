dotnet tool restore
dotnet build ./src/Build/Build.csproj --configuration Release
semVer=`dotnet gitversion /showvariable SemVer`
dotnet build ./src/EtlLauncher/EtlLauncher.csproj --configuration Release
dotnet build ./test/TestData/StageDb/StageDb.sqlproj --configuration Release
dotnet restore ./test/TestData/CoreDb/CoreDb.sqlproj --configfile ./test/TestData/CoreDb/nuget.config
dotnet build ./test/TestData/CoreDb/CoreDb.sqlproj --configuration Release --no-restore
dotnet test ./test/BuildTest/BuildTest.csproj
dotnet test ./test/IntegrationTest/IntegrationTest.csproj