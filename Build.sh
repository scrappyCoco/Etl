dotnet tool restore
dotnet build ./src/Build/Build.csproj --configuration Release
semVer=`dotnet gitversion /showvariable SemVer`
dotnet build ./src/EtlLauncher/EtlLauncher.csproj --configuration Release
dotnet build ./test/TestData/StageDb/StageDb.sqlproj --configuration Release

sourceVersionPattern="Include=\"Coding4Fun.Sql.Build\"\\s+Version=\"1.0.0\""
replacementPattern="Include=\"Coding4Fun.Sql.Build\" Version=\"${semVer}\""
$sedCommand="s/${sourceVersionPattern}/${replacementPattern}/"
sed -i '' "${sedCommand}" "Directory.Packages.props"

dotnet restore ./test/TestData/CoreDb/CoreDb.sqlproj --configfile ./test/TestData/CoreDb/nuget.config
dotnet build ./test/TestData/CoreDb/CoreDb.sqlproj --configuration Release --no-restore
dotnet test ./test/BuildTest/BuildTest.csproj
dotnet test ./test/IntegrationTest/IntegrationTest.csproj