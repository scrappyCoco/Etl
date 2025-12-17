using Coding4Fun.Etl.Build;
using Coding4Fun.Etl.Build.Services.Dacpac;
using Coding4Fun.Etl.Build.Services.IO;
using Microsoft.Build.Framework;
using Moq;

namespace Coding4Fun.Etl.BuildTest;

public class BuildEtlTaskTest
{
    private const string TestDataRoot = @"../../../../TestData/";

    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    public void Test(string configFileFormat)
    {
        const string outputEtlConfig = "/EtlCompilation/";

        List<string> logErrors = [];
        Mock<IBuildEngine> buildEngine = new();
        buildEngine.Setup(e => e
            .LogErrorEvent(It.IsAny<BuildErrorEventArgs>()))
            .Callback((BuildErrorEventArgs args) => logErrors.Add(args.Message));
        InMemoryFileProvider fileProvider = new();

        BuildEltTask buildEltTask = new()
        {
            BuildEngine = buildEngine.Object,
            ModelLoader = new SqlModelLoader(),
            FileSystemProvider = fileProvider,
            C4FEtlGeneratorSourceDacPacPath = Path.Combine(TestDataRoot, "StageDb"),
            C4FEtlGeneratorTargetDacPacPath = Path.Combine(TestDataRoot, "CoreDb"),
            C4FEtlGeneratorOutputEtlConfig = outputEtlConfig,
            C4fEtlGeneratorOutputFormat = configFileFormat
        };

        buildEltTask.Execute();

        Assert.Empty(logErrors);

        string actualGeneratedEtlConfig = fileProvider.ReadAllText(Path.Combine(outputEtlConfig, "MergePayment." + configFileFormat));
        string expectedEtlConfig = File.ReadAllText(Path.Combine(TestDataRoot, "Expected/MergePayment." + configFileFormat));
        Assert.Equal(expectedEtlConfig, actualGeneratedEtlConfig);
    }
}
