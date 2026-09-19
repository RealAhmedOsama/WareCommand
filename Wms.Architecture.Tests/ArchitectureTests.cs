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
            ["Wms.ASP"] = ["Wms.Application", "Wms.Infrastructure"],
            ["Wms.WinForms"] = ["Wms.Application", "Wms.Infrastructure"],
            ["Wms.Domain.Tests"] = ["Wms.Domain"],
            ["Wms.Application.Tests"] = ["Wms.Application", "Wms.Domain"],
            ["Wms.Infrastructure.Tests"] = ["Wms.Infrastructure", "Wms.Application", "Wms.Domain"],
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
            .SingleOrDefault(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) &&
                                     !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase));

        return projectPath ?? throw new FileNotFoundException($"Could not locate project {projectName}.");
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        var project = XDocument.Load(projectPath);
        return project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!))
            .ToArray();
    }
}
