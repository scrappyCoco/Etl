using Coding4Fun.Etl.Build.Services.Dacpac;
using Coding4Fun.Etl.Build.Services.IO;
using Microsoft.Build.Framework;

namespace Coding4Fun.Etl.BuildTest;

public class BuildEtlTaskTest
{
    private const string TestDataRoot = @"../../../../TestData/";

    [Fact]
    public void Test()
    {
        const string outputEtlConfig = "/EtlCompilation/";

        Moq.Mock<IBuildEngine> buildEngine = new();
        InMemoryFileProvider fileProvider = new();
        BuildEltTask buildEltTask = new()
        {
            BuildEngine = buildEngine.Object,
            ModelLoader = new SqlModelLoader(),
            FileSystemProvider = fileProvider,
            C4FEtlGeneratorSourceDacPacPath = Path.Combine(TestDataRoot, "StageDb"),
            C4FEtlGeneratorTargetDacPacPath = Path.Combine(TestDataRoot, "CoreDb"),
            C4FEtlGeneratorOutputEtlConfig = outputEtlConfig
        };

        buildEltTask.Execute();

        string actualGeneratedEtlConfig = fileProvider.ReadAllText(Path.Combine(outputEtlConfig, "MergePayment.json"));
        string expectedEtlConfig = File.ReadAllText(Path.Combine(TestDataRoot, "Expected/MergePayment.json"));
        Assert.Equal(expectedEtlConfig, actualGeneratedEtlConfig);
    }
}
