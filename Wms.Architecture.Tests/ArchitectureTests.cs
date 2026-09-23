using System.Xml.Linq;
using Xunit;

namespace Wms.Architecture.Tests;

public sealed class ArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wms.Domain"] = [],
            ["Wms.Application"] = ["Wms.Domain"],
            ["Wms.Infrastructure"] = ["Wms.Application"],
            ["Wms.DataMigration"] = ["Wms.Infrastructure"],
            ["Wms.ASP"] = ["Wms.Application", "Wms.Infrastructure"],
            ["Wms.WinForms"] = ["Wms.Application", "Wms.Infrastructure"],
            ["Wms.Domain.Tests"] = ["Wms.Domain"],
            ["Wms.Application.Tests"] = ["Wms.Application", "Wms.Domain"],
            ["Wms.Infrastructure.Tests"] = ["Wms.Infrastructure", "Wms.Application", "Wms.Domain"],
            ["Wms.DataMigration.Tests"] = ["Wms.DataMigration", "Wms.Infrastructure", "Wms.Domain"],
            ["Wms.Architecture.Tests"] = []
        };

    [Fact]
    public void ProjectReferencesFollowTheLayerDirection()
    {
        var root = FindRepositoryRoot();

        foreach (var (projectName, allowedReferences) in AllowedProjectReferences)
        {
            var projectPath = FindProjectPath(root, projectName);
            var actualReferences = ReadProjectReferences(projectPath);
            var forbiddenReferences = actualReferences
                .Except(allowedReferences, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.True(
                forbiddenReferences.Length == 0,
                $"{projectName} references forbidden project(s): {string.Join(", ", forbiddenReferences)}");
        }
    }

    [Theory]
    [InlineData(@"..\Wms.Domain\Wms.Domain.csproj", "Wms.Domain")]
    [InlineData("../Wms.Domain/Wms.Domain.csproj", "Wms.Domain")]
    [InlineData(@"..\Wms.Domain/Wms.Domain.csproj", "Wms.Domain")]
    public void ProjectReferenceNamesAreParsedAcrossSeparatorStyles(string include, string expectedName)
    {
        Assert.Equal(expectedName, GetProjectNameFromInclude(include));
    }

    [Fact]
    public void ProjectPathLookupIgnoresBuildOutputDirectoriesOnEveryPlatform()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wms-architecture-{Guid.NewGuid():N}");
        var sourceProject = Path.Combine(root, "src", "Wms.Domain.csproj");
        var generatedBinProject = Path.Combine(root, "src", "bin", "Debug", "Wms.Domain.csproj");
        var generatedObjProject = Path.Combine(root, "src", "obj", "Debug", "Wms.Domain.csproj");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceProject)!);
            Directory.CreateDirectory(Path.GetDirectoryName(generatedBinProject)!);
            Directory.CreateDirectory(Path.GetDirectoryName(generatedObjProject)!);
            File.WriteAllText(sourceProject, "<Project />");
            File.WriteAllText(generatedBinProject, "<Project />");
            File.WriteAllText(generatedObjProject, "<Project />");

            Assert.Equal(sourceProject, FindProjectPath(root, "Wms.Domain"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(@"C:\repo\bin\Debug\Wms.Domain.csproj", true)]
    [InlineData("/repo/obj/Debug/Wms.Domain.csproj", true)]
    [InlineData(@"C:\repo/src\obj/Debug/Wms.Domain.csproj", true)]
    [InlineData("/repo/src/bin-generated/Wms.Domain.csproj", false)]
    public void BuildOutputPathDetectionUsesPortablePathSegments(string path, bool expected)
    {
        Assert.Equal(expected, IsInBuildOutputDirectory(path));
    }

    [Fact]
    public void DomainAndApplicationDoNotReferenceOuterRuntimeLayers()
    {
        var root = FindRepositoryRoot();

        AssertSourceDoesNotContain(
            root,
            "Wms.Domain",
            "Wms.Infrastructure",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "System.Windows.Forms");

        AssertSourceDoesNotContain(
            root,
            "Wms.Application",
            "Wms.Infrastructure",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "System.Windows.Forms");
    }

    [Fact]
    public void PresentationAdaptersDoNotOwnPersistenceOrDomainConstruction()
    {
        var root = FindRepositoryRoot();
        var forbiddenPatterns = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Wms.Infrastructure.Data",
            "Wms.Infrastructure.Repositories",
            "Wms.Infrastructure.Services",
            "Wms.Domain.Entities",
            "Wms.Domain.ValueObjects",
            "Wms.Domain.Services",
            "Wms.Domain.Repositories",
            "DbContext",
            "DbSet",
            "SaveChanges",
            "new Item(",
            "new Location(",
            "new Stock(",
            "new Movement(",
            "new Warehouse("
        };

        AssertSourceDoesNotContain(root, Path.Combine("Wms.ASP", "Controllers"), forbiddenPatterns);
        AssertSourceDoesNotContain(
            root,
            Path.Combine("Warehouse Management System", "Forms"),
            forbiddenPatterns,
            includeDesignerFiles: false);
    }

    [Fact]
    public void CompositionRootsDelegateRegistrationAndInitialization()
    {
        var root = FindRepositoryRoot();
        var forbiddenPatterns = new[]
        {
            "AddDbContext",
            "AddScoped",
            "EnsureCreated",
            "SaveChanges",
            "new Item(",
            "new Location(",
            "new Stock(",
            "new Warehouse("
        };

        AssertFileDoesNotContain(root, Path.Combine("Wms.ASP", "Program.cs"), forbiddenPatterns);
        AssertFileDoesNotContain(
            root,
            Path.Combine("Warehouse Management System", "Program.cs"),
            forbiddenPatterns);
    }

    [Fact]
    public void WinFormsTransitionInventoryCoversEveryCurrentCapability()
    {
        var root = FindRepositoryRoot();
        var transition = File.ReadAllText(
            Path.Combine(root, "docs", "modernization", "WINFORMS_TRANSITION.md"));
        var expectedForms = new[]
        {
            "LoginForm.cs",
            "MainForm.cs",
            "DashboardForm.cs",
            "ReceivingForm.cs",
            "PutawayForm.cs",
            "PickingForm.cs",
            "InventoryForm.cs",
            "StockAdjustmentDialog.cs",
            "ItemManagementForm.cs",
            "ItemEditDialog.cs",
            "LocationManagementForm.cs",
            "LocationEditDialog.cs",
            "ReportsForm.cs"
        };

        foreach (var form in expectedForms)
        {
            Assert.Contains($"`{form}`", transition, StringComparison.Ordinal);
        }

        Assert.Contains("F1–F8", transition, StringComparison.Ordinal);
        Assert.Contains("Enter", transition, StringComparison.Ordinal);
        Assert.Contains("Arabic/English", transition, StringComparison.Ordinal);
    }

    [Fact]
    public void WinFormsOperationalPathsUseSharedAuthenticatedBoundaries()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "Warehouse Management System", "Program.cs"));
        Assert.Contains("AddWmsInfrastructure", program, StringComparison.Ordinal);
        Assert.Contains("AddWmsApplication", program, StringComparison.Ordinal);
        Assert.Contains("AddWmsDesktopIdentity", program, StringComparison.Ordinal);
        Assert.Contains("LoginForm", program, StringComparison.Ordinal);

        var requiredMarkers = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ReceivingForm.cs"] = ["IReceiveItemUseCase", "RequireUserId"],
            ["PutawayForm.cs"] = ["IPutawayUseCase", "RequireUserId"],
            ["PickingForm.cs"] = ["IPickOrderUseCase", "RequireUserId"],
            ["InventoryForm.cs"] = ["IStockAdjustmentUseCase", "RequireUserId"],
            ["ItemEditDialog.cs"] = ["ICreateItemUseCase", "RequireUserId"],
            ["LocationEditDialog.cs"] = ["ICreateLocationUseCase", "RequireUserId"]
        };

        foreach (var (fileName, markers) in requiredMarkers)
        {
            var source = File.ReadAllText(Path.Combine(root, "Warehouse Management System", "Forms", fileName));
            foreach (var marker in markers)
            {
                Assert.Contains(marker, source, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("\"SYSTEM\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("WEB_USER", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CurrentApplicationFoldersMapToCapabilityModules()
    {
        var root = FindRepositoryRoot();
        var moduleFolders = new[]
        {
            "Inventory",
            "Items",
            "Locations",
            "Picking",
            "Receiving",
            "Reports"
        };

        foreach (var moduleFolder in moduleFolders)
        {
            var path = Path.Combine(root, "Wms.Application", "UseCases", moduleFolder);
            Assert.True(Directory.Exists(path), $"Missing application capability folder: {path}");
        }
    }

    private static void AssertSourceDoesNotContain(
        string root,
        string relativeDirectory,
        params string[] forbiddenPatterns)
    {
        AssertSourceDoesNotContain(root, relativeDirectory, forbiddenPatterns, includeDesignerFiles: true);
    }

    private static void AssertSourceDoesNotContain(
        string root,
        string relativeDirectory,
        string[] forbiddenPatterns,
        bool includeDesignerFiles)
    {
        var directory = Path.Combine(root, relativeDirectory);
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => includeDesignerFiles ||
                           !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var forbiddenPattern in forbiddenPatterns)
            {
                Assert.False(
                    source.Contains(forbiddenPattern, StringComparison.Ordinal),
                    $"{file} contains forbidden architecture dependency or construction: {forbiddenPattern}");
            }
        }
    }

    private static void AssertFileDoesNotContain(
        string root,
        string relativeFile,
        string[] forbiddenPatterns)
    {
        var file = Path.Combine(root, relativeFile);
        var source = File.ReadAllText(file);
        foreach (var forbiddenPattern in forbiddenPatterns)
        {
            Assert.False(
                source.Contains(forbiddenPattern, StringComparison.Ordinal),
                $"{file} contains composition-root responsibility that belongs in a layer extension: {forbiddenPattern}");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Warehouse Management System.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    private static string FindProjectPath(string root, string projectName)
    {
        var projectPath = Directory
            .EnumerateFiles(root, $"{projectName}.csproj", SearchOption.AllDirectories)
            .SingleOrDefault(path => !IsInBuildOutputDirectory(path));

        return projectPath ?? throw new FileNotFoundException($"Could not locate project {projectName}.");
    }

    private static bool IsInBuildOutputDirectory(string path)
    {
        return path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetProjectNameFromInclude(string include)
    {
        return Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        var project = XDocument.Load(projectPath);
        return project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => GetProjectNameFromInclude(include!))
            .ToArray();
    }
}
