using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using CommandLine;

namespace Mister.Version.CLI
{
    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<ReportOptions, VersionOptions>(args)
                .MapResult(
                    (ReportOptions opts) => RunReportCommand(opts),
                    (VersionOptions opts) => RunVersionCommand(opts),
                    errs => 1);
        }

        static int RunReportCommand(ReportOptions options)
        {
            try
            {
                var reporter = new VersionReporter(options);
                return reporter.GenerateReport();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        static int RunVersionCommand(VersionOptions options)
        {
            try
            {
                var calculator = new VersionCalculator(options);
                return calculator.CalculateVersion();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }

    [Verb("report", HelpText = "Generate a version report for projects in the repository")]
    public class ReportOptions
    {
        [Option('r', "repo", Required = false, HelpText = "Path to the Git repository root", Default = ".")]
        public string RepoPath { get; set; }

        [Option('p', "project-dir", Required = false, HelpText = "Path to the directory containing projects", Default = ".")]
        public string ProjectDir { get; set; }

        [Option('o', "output", Required = false, HelpText = "Output format: text, json, or csv", Default = "text")]
        public string OutputFormat { get; set; }

        [Option('f', "file", Required = false, HelpText = "Output file path (if not specified, outputs to console)")]
        public string OutputFile { get; set; }

        [Option('b', "branch", Required = false, HelpText = "Branch to report versions for (defaults to current branch)")]
        public string Branch { get; set; }

        [Option('t', "tag-prefix", Required = false, HelpText = "Prefix for version tags", Default = "v")]
        public string TagPrefix { get; set; }

        [Option("include-commits", Required = false, HelpText = "Include commit information in the report", Default = true)]
        public bool IncludeCommits { get; set; }

        [Option("include-dependencies", Required = false, HelpText = "Include dependency information in the report", Default = true)]
        public bool IncludeDependencies { get; set; }
    }

    [Verb("version", HelpText = "Calculate the version for a specific project")]
    public class VersionOptions
    {
        [Option('r', "repo", Required = false, HelpText = "Path to the Git repository root", Default = ".")]
        public string RepoPath { get; set; }

        [Option('p', "project", Required = true, HelpText = "Path to the project file")]
        public string ProjectPath { get; set; }

        [Option('t', "tag-prefix", Required = false, HelpText = "Prefix for version tags", Default = "v")]
        public string TagPrefix { get; set; }

        [Option('d', "detailed", Required = false, HelpText = "Show detailed information about version calculation", Default = false)]
        public bool Detailed { get; set; }

        [Option('j', "json", Required = false, HelpText = "Output in JSON format", Default = false)]
        public bool JsonOutput { get; set; }
    }

    public class VersionReporter
    {
        private readonly ReportOptions _options;
        private Repository _repo;
        private SemVer _globalVersion;
        private Dictionary<string, ProjectInfo> _projectInfos = new Dictionary<string, ProjectInfo>();
        private Dictionary<string, List<string>> _projectDependencies = new Dictionary<string, List<string>>();

        public VersionReporter(ReportOptions options)
        {
            _options = options;
        }

        public int GenerateReport()
        {
            // Initialize repository
            _repo = new Repository(_options.RepoPath);

            // Get current branch or specified branch
            Branch branch = null;
            if (string.IsNullOrEmpty(_options.Branch))
            {
                branch = _repo.Head;
            }
            else
            {
                branch = _repo.Branches[_options.Branch];
                if (branch == null)
                {
                    Console.Error.WriteLine($"Branch '{_options.Branch}' not found");
                    return 1;
                }
            }

            // Determine branch type
            var branchType = DetermineBranchType(branch.FriendlyName);
            Console.WriteLine($"Analyzing {branch.FriendlyName} ({branchType})");

            // Get global version tag
            _globalVersion = GetGlobalVersion(branchType);
            Console.WriteLine($"Global version: {_globalVersion?.Major}.{_globalVersion?.Minor}.{_globalVersion?.Patch}");

            // Find and process all project files
            var projectDir = Path.Combine(_options.RepoPath, _options.ProjectDir);
            var projectFiles = Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories);
            Console.WriteLine($"Found {projectFiles.Length} projects");

            // Process each project
            foreach (var projectFile in projectFiles)
            {
                ProcessProject(projectFile, branchType);
            }

            // Process dependencies if requested
            if (_options.IncludeDependencies)
            {
                ProcessDependencies();
            }

            // Generate the report
            GenerateOutputReport();

            return 0;
        }

        private void ProcessProject(string projectFile, BranchType branchType)
        {
            try
            {
                // Get project name
                var projectName = Path.GetFileNameWithoutExtension(projectFile);

                // Check if the project should be skipped
                bool isTestProject = false;
                bool isPackable = true;

                var projectContent = File.ReadAllText(projectFile);
                if (projectContent.Contains("<IsTestProject>true</IsTestProject>"))
                {
                    isTestProject = true;
                }
                if (projectContent.Contains("<IsPackable>false</IsPackable>"))
                {
                    isPackable = false;
                }

                // Skip test projects and non-packable projects in the report if they would be skipped
                if ((isTestProject || !isPackable) && !projectContent.Contains("<MonoRepoVersioningEnabled>true</MonoRepoVersioningEnabled>"))
                {
                    return;
                }

                // Get relative path from repo root
                var relativePath = Path.GetRelativePath(_options.RepoPath, projectFile);

                // Get project-specific version tag
                var projectTag = GetProjectSpecificVersion(projectName, branchType);

                // Get the current version
                VersionInfo version;
                if (projectTag != null)
                {
                    version = new VersionInfo
                    {
                        Version = $"{projectTag.Major}.{projectTag.Minor}.{projectTag.Patch}",
                        CommitSha = projectTag.CommitSha,
                        CommitDate = projectTag.CommitDate,
                        CommitMessage = projectTag.CommitMessage
                    };
                }
                else
                {
                    version = new VersionInfo
                    {
                        Version = $"{_globalVersion.Major}.{_globalVersion.Minor}.{_globalVersion.Patch}",
                        CommitSha = null,
                        CommitDate = null,
                        CommitMessage = "No project-specific tag found, using global version"
                    };
                }

                // Collect dependencies
                var dependencies = new List<string>();
                var matches = Regex.Matches(projectContent, @"<ProjectReference Include=""([^""]+)""");
                foreach (Match match in matches)
                {
                    var dependencyPath = match.Groups[1].Value;
                    var dependencyName = Path.GetFileNameWithoutExtension(dependencyPath);
                    dependencies.Add(dependencyName);
                }

                // Store dependencies for later processing
                _projectDependencies[projectName] = dependencies;

                // Store project info
                _projectInfos[projectName] = new ProjectInfo
                {
                    Name = projectName,
                    Path = relativePath,
                    CurrentVersion = version,
                    Dependencies = dependencies,
                    IsTestProject = isTestProject,
                    IsPackable = isPackable
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing project {projectFile}: {ex.Message}");
            }
        }

        private void ProcessDependencies()
        {
            // For each project, calculate its dependency tree
            foreach (var project in _projectInfos.Values)
            {
                project.DependencyTree = BuildDependencyTree(project.Name, new HashSet<string>());
            }
        }

        private List<string> BuildDependencyTree(string projectName, HashSet<string> visited)
        {
            if (visited.Contains(projectName))
            {
                return new List<string>(); // Avoid circular dependencies
            }

            visited.Add(projectName);
            var result = new List<string>();

            if (_projectDependencies.TryGetValue(projectName, out var dependencies))
            {
                foreach (var dependency in dependencies)
                {
                    result.Add(dependency);
                    result.AddRange(BuildDependencyTree(dependency, new HashSet<string>(visited)));
                }
            }

            return result.Distinct().ToList();
        }

        private void GenerateOutputReport()
        {
            string output;

            switch (_options.OutputFormat.ToLower())
            {
                case "json":
                    output = GenerateJsonReport();
                    break;
                case "csv":
                    output = GenerateCsvReport();
                    break;
                case "text":
                default:
                    output = GenerateTextReport();
                    break;
            }

            if (string.IsNullOrEmpty(_options.OutputFile))
            {
                Console.WriteLine(output);
            }
            else
            {
                File.WriteAllText(_options.OutputFile, output);
                Console.WriteLine($"Report written to {_options.OutputFile}");
            }
        }

        private string GenerateTextReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== MonoRepo Version Report ===");
            sb.AppendLine($"Repository: {_options.RepoPath}");
            sb.AppendLine($"Global Version: {_globalVersion?.Major}.{_globalVersion?.Minor}.{_globalVersion?.Patch}");
            sb.AppendLine($"Projects: {_projectInfos.Count}");
            sb.AppendLine();

            var orderedProjects = _projectInfos.Values.OrderBy(p => p.Name).ToList();
            foreach (var project in orderedProjects)
            {
                sb.AppendLine($"Project: {project.Name}");
                sb.AppendLine($"  Path: {project.Path}");
                sb.AppendLine($"  Version: {project.CurrentVersion.Version}");

                if (_options.IncludeCommits && !string.IsNullOrEmpty(project.CurrentVersion.CommitSha))
                {
                    sb.AppendLine($"  Commit: {project.CurrentVersion.CommitSha}");
                    sb.AppendLine($"  Date: {project.CurrentVersion.CommitDate}");
                    sb.AppendLine($"  Message: {project.CurrentVersion.CommitMessage}");
                }

                if (_options.IncludeDependencies && project.Dependencies.Count > 0)
                {
                    sb.AppendLine($"  Direct Dependencies ({project.Dependencies.Count}):");
                    foreach (var dep in project.Dependencies)
                    {
                        var depVersion = _projectInfos.TryGetValue(dep, out var depInfo) ? depInfo.CurrentVersion.Version : "Unknown";
                        sb.AppendLine($"    - {dep} ({depVersion})");
                    }

                    if (project.DependencyTree?.Count > project.Dependencies.Count)
                    {
                        sb.AppendLine($"  All Dependencies ({project.DependencyTree.Count}):");
                        foreach (var dep in project.DependencyTree)
                        {
                            if (!project.Dependencies.Contains(dep))
                            {
                                var depVersion = _projectInfos.TryGetValue(dep, out var depInfo) ? depInfo.CurrentVersion.Version : "Unknown";
                                sb.AppendLine($"    - {dep} ({depVersion}) [Transitive]");
                            }
                        }
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string GenerateJsonReport()
        {
            var report = new
            {
                Repository = _options.RepoPath,
                GlobalVersion = $"{_globalVersion?.Major}.{_globalVersion?.Minor}.{_globalVersion?.Patch}",
                ProjectCount = _projectInfos.Count,
                Projects = _projectInfos.Values.OrderBy(p => p.Name).Select(p => new
                {
                    p.Name,
                    p.Path,
                    Version = p.CurrentVersion.Version,
                    Commit = _options.IncludeCommits ? new
                    {
                        Sha = p.CurrentVersion.CommitSha,
                        Date = p.CurrentVersion.CommitDate,
                        Message = p.CurrentVersion.CommitMessage
                    } : null,
                    Dependencies = _options.IncludeDependencies ? new
                    {
                        Direct = p.Dependencies.Select(d => new
                        {
                            Name = d,
                            Version = _projectInfos.TryGetValue(d, out var depInfo) ? depInfo.CurrentVersion.Version : "Unknown"
                        }),
                        All = p.DependencyTree?.Select(d => new
                        {
                            Name = d,
                            Version = _projectInfos.TryGetValue(d, out var depInfo) ? depInfo.CurrentVersion.Version : "Unknown",
                            IsTransitive = !p.Dependencies.Contains(d)
                        })
                    } : null,
                    p.IsTestProject,
                    p.IsPackable
                })
            };

            return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        }

        private string GenerateCsvReport()
        {
            var sb = new StringBuilder();

            // Write header
            sb.AppendLine("ProjectName,ProjectPath,Version,CommitSha,CommitDate,CommitMessage,IsTestProject,IsPackable,Dependencies");

            // Write data
            foreach (var project in _projectInfos.Values.OrderBy(p => p.Name))
            {
                sb.Append($"{EscapeCsv(project.Name)},");
                sb.Append($"{EscapeCsv(project.Path)},");
                sb.Append($"{EscapeCsv(project.CurrentVersion.Version)},");

                if (_options.IncludeCommits)
                {
                    sb.Append($"{EscapeCsv(project.CurrentVersion.CommitSha)},");
                    sb.Append($"{EscapeCsv(project.CurrentVersion.CommitDate)},");
                    sb.Append($"{EscapeCsv(project.CurrentVersion.CommitMessage)},");
                }
                else
                {
                    sb.Append(",,");
                }

                sb.Append($"{project.IsTestProject},");
                sb.Append($"{project.IsPackable},");

                if (_options.IncludeDependencies)
                {
                    sb.Append($"{EscapeCsv(string.Join(";", project.Dependencies))}");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // If the value contains a comma, quote, or newline, enclose it in quotes and escape embedded quotes
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private BranchType DetermineBranchType(string branchName)
        {
            if (branchName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                branchName.Equals("master", StringComparison.OrdinalIgnoreCase))
            {
                return BranchType.Main;
            }
            else if (branchName.StartsWith("release/", StringComparison.OrdinalIgnoreCase) ||
                     Regex.IsMatch(branchName, @"^v\d+\.\d+(\.\d+)?$", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(branchName, @"^release-\d+\.\d+(\.\d+)?$", RegexOptions.IgnoreCase))
            {
                return BranchType.Release;
            }
            else
            {
                return BranchType.Feature;
            }
        }

        private SemVer GetGlobalVersion(BranchType branchType)
        {
            // Get all global version tags (those without a project suffix)
            var globalVersionTags = _repo.Tags
                .Where(t => t.FriendlyName.StartsWith(_options.TagPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(t => !t.FriendlyName.Contains("-")) // Exclude project-specific tags
                .Select(t => new
                {
                    Tag = t,
                    Version = ParseSemVer(t.FriendlyName.Substring(_options.TagPrefix.Length))
                })
                .Where(t => t.Version != null)
                .OrderByDescending(t => t.Version.Major)
                .ThenByDescending(t => t.Version.Minor)
                .ThenByDescending(t => t.Version.Patch)
                .ToList();

            // Filter tags based on branch type
            if (branchType == BranchType.Release)
            {
                // For release branches, find tags matching the release version
                var releaseVersion = ExtractReleaseVersion(_repo.Head.FriendlyName, _options.TagPrefix);
                if (releaseVersion != null)
                {
                    globalVersionTags = globalVersionTags
                        .Where(t => t.Version.Major == releaseVersion.Major &&
                                    t.Version.Minor == releaseVersion.Minor)
                        .ToList();
                }
            }

            // Return the most recent global version tag
            if (globalVersionTags.Any())
            {
                var tag = globalVersionTags.First();
                return tag.Version;
            }

            // If no global tag is found, create a default version
            return new SemVer { Major = 0, Minor = 1, Patch = 0 };
        }

        private TagVersionInfo GetProjectSpecificVersion(string projectName, BranchType branchType)
        {
            var projectSuffix = $"-{projectName.ToLowerInvariant()}";

            // Get all project-specific version tags
            var projectVersionTags = _repo.Tags
                .Where(t => t.FriendlyName.StartsWith(_options.TagPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(t => t.FriendlyName.ToLowerInvariant().EndsWith(projectSuffix))
                .Select(t =>
                {
                    var tagName = t.FriendlyName;
                    var versionPart = tagName.Substring(_options.TagPrefix.Length, tagName.Length - _options.TagPrefix.Length - projectSuffix.Length);
                    var version = ParseSemVer(versionPart);

                    if (version == null)
                        return null;

                    var commit = t.Target as Commit;
                    if (commit == null)
                        return null;

                    return new TagVersionInfo
                    {
                        Major = version.Major,
                        Minor = version.Minor,
                        Patch = version.Patch,
                        CommitSha = commit.Sha.Substring(0, 7),
                        CommitDate = commit.Author.When.ToString("yyyy-MM-dd HH:mm:ss"),
                        CommitMessage = commit.MessageShort
                    };
                })
                .Where(t => t != null)
                .OrderByDescending(t => t.Major)
                .ThenByDescending(t => t.Minor)
                .ThenByDescending(t => t.Patch)
                .ToList();

            // Filter tags based on branch type
            if (branchType == BranchType.Release)
            {
                // For release branches, find tags matching the release version
                var releaseVersion = ExtractReleaseVersion(_repo.Head.FriendlyName, _options.TagPrefix);
                if (releaseVersion != null)
                {
                    projectVersionTags = projectVersionTags
                        .Where(t => t.Major == releaseVersion.Major &&
                                    t.Minor == releaseVersion.Minor)
                        .ToList();
                }
            }

            // Return the most recent project-specific version tag
            if (projectVersionTags.Any())
            {
                return projectVersionTags.First();
            }

            return null;
        }

        private SemVer ExtractReleaseVersion(string branchName, string tagPrefix)
        {
            // Extract version from branch name (format: release/X.Y.Z or vX.Y.Z or release-X.Y.Z)
            string versionPart = branchName;

            if (branchName.StartsWith("release/", StringComparison.OrdinalIgnoreCase))
            {
                versionPart = branchName.Substring("release/".Length);
            }
            else if (branchName.StartsWith("release-", StringComparison.OrdinalIgnoreCase))
            {
                versionPart = branchName.Substring("release-".Length);
            }

            if (versionPart.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                versionPart = versionPart.Substring(tagPrefix.Length);
            }

            return ParseSemVer(versionPart);
        }

        private SemVer ParseSemVer(string version)
        {
            var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?");
            if (!match.Success)
                return null;

            return new SemVer
            {
                Major = int.Parse(match.Groups[1].Value),
                Minor = int.Parse(match.Groups[2].Value),
                Patch = match.Groups.Count > 3 && match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0
            };
        }
    }

    public class VersionCalculator
    {
        private readonly VersionOptions _options;

        public VersionCalculator(VersionOptions options)
        {
            _options = options;
        }

        public int CalculateVersion()
        {
            try
            {
                // This would reuse much of the logic from the MonoRepoVersionTask
                // but simplified for CLI usage
                Console.WriteLine($"Calculating version for project: {_options.ProjectPath}");

                // Implement the version calculation logic
                // For now, returning a placeholder
                if (_options.JsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        Project = Path.GetFileNameWithoutExtension(_options.ProjectPath),
                        Version = "1.0.0", // Placeholder
                        CommitSha = "abcdef123", // Placeholder
                        CommitDate = DateTime.Now.ToString("yyyy-MM-dd")
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Console.WriteLine($"Version: 1.0.0");
                    Console.WriteLine($"Commit: abcdef123");
                    Console.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error calculating version: {ex.Message}");
                return 1;
            }
        }
    }

    public class SemVer
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
    }

    public class TagVersionInfo
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
        public string CommitSha { get; set; }
        public string CommitDate { get; set; }
        public string CommitMessage { get; set; }
    }

    public class VersionInfo
    {
        public string Version { get; set; }
        public string CommitSha { get; set; }
        public string CommitDate { get; set; }
        public string CommitMessage { get; set; }
    }

    public class ProjectInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public VersionInfo CurrentVersion { get; set; }
        public List<string> Dependencies { get; set; } = new List<string>();
        public List<string> DependencyTree { get; set; } = new List<string>();
        public bool IsTestProject { get; set; }
        public bool IsPackable { get; set; }
    }

    public enum BranchType
    {
        Main,
        Release,
        Feature
    }
}