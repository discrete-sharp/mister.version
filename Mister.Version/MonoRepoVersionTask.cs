using LibGit2Sharp;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Mister.Version;

/// <summary>
/// MSBuild task that automatically versions projects in a monorepo based on git history
/// and change detection rules without requiring version changes to be committed
/// </summary>
public class MonoRepoVersionTask : Task
{
    /// <summary>
    /// Path to the project file being built
    /// </summary>
    [Required]
    public string ProjectPath { get; set; }

    /// <summary>
    /// Path to the root of the monorepo
    /// </summary>
    [Required]
    public string RepoRoot { get; set; }

    /// <summary>
    /// Output parameter for the calculated version
    /// </summary>
    [Output]
    public string Version { get; set; }

    /// <summary>
    /// Output parameter that indicates if changes were detected for this project
    /// </summary>
    [Output]
    public bool VersionChanged { get; set; }

    /// <summary>
    /// Whether to automatically update the project file with the new version
    /// This is set to FALSE by default to avoid requiring commits for version changes
    /// </summary>
    public bool UpdateProjectFile { get; set; } = false;

    /// <summary>
    /// Optional parameter to force a specific version
    /// </summary>
    public string ForceVersion { get; set; }

    /// <summary>
    /// List of project dependencies to check for changes
    /// </summary>
    public ITaskItem[] Dependencies { get; set; }

    /// <summary>
    /// Custom tag prefix for version tags (default: v)
    /// </summary>
    public string TagPrefix { get; set; } = "v";

    /// <summary>
    /// Debug mode for verbose logging
    /// </summary>
    public bool Debug { get; set; } = false;

    /// <summary>
    /// Extra debug info to display dependency graph and change details
    /// </summary>
    public bool ExtraDebug { get; set; } = false;

    /// <summary>
    /// Whether to skip versioning for test projects
    /// </summary>
    public bool SkipTestProjects { get; set; } = true;

    /// <summary>
    /// Whether to skip versioning for non-packable projects
    /// </summary>
    public bool SkipNonPackableProjects { get; set; } = true;

    /// <summary>
    /// Whether this project is a test project
    /// </summary>
    public bool IsTestProject { get; set; } = false;

    /// <summary>
    /// Whether this project is packable
    /// </summary>
    public bool IsPackable { get; set; } = true;

    public override bool Execute()
    {
        try
        {
            Log.LogMessage(MessageImportance.High, $"Starting MisterVersion versioning for {ProjectPath}");
            VersionChanged = false; // Initialize to false by default

            // Check if we should skip versioning for this project
            if (SkipTestProjects && IsTestProject)
            {
                Log.LogMessage(MessageImportance.High, $"Skipping versioning for test project: {Path.GetFileName(ProjectPath)}");
                // Set a default version to avoid build errors
                Version = "1.0.0";
                return true;
            }

            if (SkipNonPackableProjects && !IsPackable)
            {
                Log.LogMessage(MessageImportance.High, $"Skipping versioning for non-packable project: {Path.GetFileName(ProjectPath)}");
                // Set a default version to avoid build errors
                Version = "1.0.0";
                return true;
            }

            // If a version is forced, use it directly
            if (!string.IsNullOrEmpty(ForceVersion))
            {
                Log.LogMessage(MessageImportance.Normal, $"Using forced version: {ForceVersion}");
                Version = ForceVersion;

                if (UpdateProjectFile)
                {
                    UpdateProjectFileVersion();
                }

                return true;
            }

            // Initialize repository
            using (var repo = new Repository(RepoRoot))
            {
                // Get current branch
                var currentBranch = repo.Head.FriendlyName;
                Log.LogMessage(MessageImportance.Normal, $"Current branch: {currentBranch}");

                // Determine branch type (main, release, feature)
                var branchType = DetermineBranchType(currentBranch);
                Log.LogMessage(MessageImportance.Normal, $"Branch type: {branchType}");

                // Get project directory relative to repo root
                var projectDir = NormalizePath(Path.GetDirectoryName(ProjectPath));
#if NET472
                var relativeProjectPath = NormalizePath(PathUtils.GetRelativePath(RepoRoot, projectDir));
#else
                var relativeProjectPath = NormalizePath(Path.GetRelativePath(RepoRoot, projectDir));
#endif
                var projectName = Path.GetFileNameWithoutExtension(ProjectPath);

                Log.LogMessage(MessageImportance.Normal, $"Project path: {relativeProjectPath}");

                // Print detailed dependency information if in extra debug mode
                if (ExtraDebug)
                {
                    LogDependencyGraph();

                    // Add detailed file inspection for the project path
                    LogFilesInProjectPath(repo, relativeProjectPath);
                }

                // First, look for repo-wide version tags (e.g. v8.2.0)
                var globalVersionTag = GetGlobalVersionTag(repo, branchType);

                // Then, check for project-specific tags
                var projectVersionTag = GetProjectVersionTag(repo, projectName, branchType);

                // Combine the information from both tag types
                var baseVersionTag = DetermineBaseVersion(globalVersionTag, projectVersionTag, branchType);

                if (Debug || ExtraDebug)
                {
                    LogAllTags(repo);

                    // Show commit count and history information
                    if (baseVersionTag?.Commit != null)
                    {
                        LogCommitsSinceTag(repo, baseVersionTag.Commit);

                        // Add change tree inspection
                        if (ExtraDebug)
                        {
                            LogChangeTreeDetails(repo, baseVersionTag.Commit, relativeProjectPath);
                            AnalyzeChangedFiles(repo, baseVersionTag.Commit, relativeProjectPath);
                        }
                    }
                }

                // Calculate the new version based on changes
                Version = CalculateVersion(repo, baseVersionTag, relativeProjectPath, projectName, branchType);
                Log.LogMessage(MessageImportance.High, $"Calculated version: {Version} for {Path.GetFileName(ProjectPath)}");

                // Update the project file if enabled (disabled by default)
                if (UpdateProjectFile)
                {
                    UpdateProjectFileVersion();
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }

    /// <summary>
    /// Debug method to log all tags found in the repository
    /// </summary>
    private void LogAllTags(Repository repo)
    {
        Log.LogMessage(MessageImportance.High, "--- DEBUG: All tags in repository ---");
        foreach (var tag in repo.Tags)
        {
            Log.LogMessage(MessageImportance.High, $"Tag: {tag.FriendlyName}, Target: {tag.Target.Sha.Substring(0, 7)}");
        }
        Log.LogMessage(MessageImportance.High, "--- END DEBUG ---");
    }

    /// <summary>
    /// Determines the type of branch based on its name
    /// </summary>
    internal BranchType DetermineBranchType(string branchName)
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

    /// <summary>
    /// Gets the global version tag (e.g. v8.2.0) which applies to the entire repo
    /// </summary>
    private VersionTag GetGlobalVersionTag(Repository repo, BranchType branchType)
    {
        // Get all global version tags (those without a project suffix)
        var globalVersionTags = repo.Tags
            .Where(t => t.FriendlyName.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.FriendlyName.Contains("-")) // Exclude project-specific tags
            .Select(t => new VersionTag
            {
                Tag = t,
                SemVer = ParseSemVer(t.FriendlyName.Substring(TagPrefix.Length)),
                Commit = t.Target as Commit,
                IsGlobal = true
            })
            .Where(vt => vt.SemVer != null)
            .OrderByDescending(vt => vt.SemVer.Major)
            .ThenByDescending(vt => vt.SemVer.Minor)
            .ThenByDescending(vt => vt.SemVer.Patch)
            .ToList();

        if (Debug)
        {
            Log.LogMessage(MessageImportance.High, $"Found {globalVersionTags.Count} global version tags");
            foreach (var tag in globalVersionTags.Take(5))
            {
                Log.LogMessage(MessageImportance.High, $"  Global Tag: {tag.Tag.FriendlyName}, Version: {tag.SemVer.Major}.{tag.SemVer.Minor}.{tag.SemVer.Patch}");
            }
        }

        // Filter tags based on branch type
        if (branchType == BranchType.Release)
        {
            // For release branches, find tags matching the release version
            var releaseVersion = ExtractReleaseVersion(repo.Head.FriendlyName);
            if (releaseVersion != null)
            {
                globalVersionTags = globalVersionTags
                    .Where(vt => vt.SemVer.Major == releaseVersion.Major &&
                                vt.SemVer.Minor == releaseVersion.Minor)
                    .ToList();
            }
        }

        // Return the most recent global version tag
        if (globalVersionTags.Any())
        {
            var tag = globalVersionTags.First();
            Log.LogMessage(MessageImportance.Normal, $"Found global version tag: {tag.Tag.FriendlyName}");
            return tag;
        }

        // If no global tag is found, create a default version
        Log.LogMessage(MessageImportance.Normal, "No global version tag found, defaulting to 0.1.0");
        return new VersionTag
        {
            SemVer = new SemVer { Major = 0, Minor = 1, Patch = 0 },
            IsGlobal = true
        };
    }

    /// <summary>
    /// Gets the project-specific version tag (e.g. v8.2.1-project)
    /// </summary>
    private VersionTag GetProjectVersionTag(Repository repo, string projectName, BranchType branchType)
    {
        var projectSuffix = $"-{projectName.ToLowerInvariant()}";

        // Get all project-specific version tags
        var projectVersionTags = repo.Tags
            .Where(t => t.FriendlyName.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.FriendlyName.ToLowerInvariant().EndsWith(projectSuffix))
            .Select(t =>
            {
                var tagName = t.FriendlyName;
                var versionPart = tagName.Substring(TagPrefix.Length, tagName.Length - TagPrefix.Length - projectSuffix.Length);

                return new VersionTag
                {
                    Tag = t,
                    SemVer = ParseSemVer(versionPart),
                    Commit = t.Target as Commit,
                    IsGlobal = false
                };
            })
            .Where(vt => vt.SemVer != null)
            .OrderByDescending(vt => vt.SemVer.Major)
            .ThenByDescending(vt => vt.SemVer.Minor)
            .ThenByDescending(vt => vt.SemVer.Patch)
            .ToList();

        if (Debug)
        {
            Log.LogMessage(MessageImportance.High, $"Found {projectVersionTags.Count} project-specific version tags for {projectName}");
            foreach (var tag in projectVersionTags.Take(5))
            {
                Log.LogMessage(MessageImportance.High, $"  Project Tag: {tag.Tag.FriendlyName}, Version: {tag.SemVer.Major}.{tag.SemVer.Minor}.{tag.SemVer.Patch}");
            }
        }

        // Filter tags based on branch type
        if (branchType == BranchType.Release)
        {
            // For release branches, find tags matching the release version
            var releaseVersion = ExtractReleaseVersion(repo.Head.FriendlyName);
            if (releaseVersion != null)
            {
                projectVersionTags = projectVersionTags
                    .Where(vt => vt.SemVer.Major == releaseVersion.Major &&
                                vt.SemVer.Minor == releaseVersion.Minor)
                    .ToList();
            }
        }

        // Return the most recent project-specific version tag
        if (projectVersionTags.Any())
        {
            var tag = projectVersionTags.First();
            Log.LogMessage(MessageImportance.Normal, $"Found project-specific version tag: {tag.Tag.FriendlyName}");
            return tag;
        }

        // If no project-specific tag is found, return null
        Log.LogMessage(MessageImportance.Normal, $"No project-specific version tag found for {projectName}");
        return null;
    }

    /// <summary>
    /// Determines the base version to use based on global and project-specific tags
    /// </summary>
    private VersionTag DetermineBaseVersion(VersionTag globalTag, VersionTag projectTag, BranchType branchType)
    {
        // If we have a project-specific tag, use it as the base
        if (projectTag != null)
        {
            // Check if the project tag is based on the current global tag
            if (projectTag.SemVer.Major == globalTag.SemVer.Major &&
                projectTag.SemVer.Minor == globalTag.SemVer.Minor)
            {
                // Use the project tag, it's more specific
                return projectTag;
            }
        }

        // Otherwise, use the global tag
        return globalTag;
    }

    /// <summary>
    /// Calculate the new version based on changes and branch type with more selective versioning
    /// </summary>
    private string CalculateVersion(Repository repo, VersionTag baseVersionTag, string projectPath, string projectName, BranchType branchType)
    {
        var newVersion = new SemVer
        {
            Major = baseVersionTag.SemVer.Major,
            Minor = baseVersionTag.SemVer.Minor,
            Patch = baseVersionTag.SemVer.Patch
        };

        // Check if the project has any changes since the base tag
        bool hasChanges = false;
        if (baseVersionTag.Commit != null)
        {
            hasChanges = ProjectHasChangedSinceTag(repo, baseVersionTag.Commit, projectPath);
        }

        // For any branch type, only change version if actual changes detected
        if (hasChanges)
        {
            VersionChanged = true;

            // For main branch:
            // - Commits always increment patch version
            // Major/Minor are controlled manually
            if (branchType == BranchType.Main)
            {
                newVersion.Patch++;
                Log.LogMessage(MessageImportance.Normal, $"Main branch: Incrementing patch version to {newVersion.Patch} due to changes");
            }
            // For release branches:
            // - Patch number is incremented for changes
            else if (branchType == BranchType.Release)
            {
                var releaseVersion = ExtractReleaseVersion(repo.Head.FriendlyName);
                if (releaseVersion != null)
                {
                    newVersion.Major = releaseVersion.Major;
                    newVersion.Minor = releaseVersion.Minor;
                    newVersion.Patch++;
                    Log.LogMessage(MessageImportance.Normal, $"Release branch: Using version {newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}");
                }
            }
            // For feature branches:
            // - Only changed projects get the feature branch version
            // - Others keep their base tag version
            else if (branchType == BranchType.Feature)
            {
                var branchNameNormalized = repo.Head.FriendlyName
                    .Replace("/", "-")
                    .Replace("_", "-")
                    .ToLowerInvariant();

                // Generate a hash for uniqueness
                var hashPart = CalculateCommitShortHash(repo.Head.Tip);

                // Only use pre-release tag for changed projects
                return $"{newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}-{branchNameNormalized}.{hashPart}";
            }
        }
        else
        {
            // No changes detected, keep the base version
            if (branchType == BranchType.Feature)
            {
                // Special case for feature branches if project hasn't changed
                // Use the base version from the tag without feature branch suffix
                Log.LogMessage(MessageImportance.Normal, $"Feature branch but no changes detected, using base version {newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}");
            }
            else
            {
                Log.LogMessage(MessageImportance.Normal, $"No changes detected, using base version {newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}");
            }
        }

        return $"{newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}";
    }

    /// <summary>
    /// Updates the project file with the calculated version
    /// Note: This is disabled by default to avoid requiring version changes to be committed
    /// </summary>
    private void UpdateProjectFileVersion()
    {
        try
        {
            var projectFile = File.ReadAllText(ProjectPath);

            // Update or add the Version element
            var versionRegex = new Regex(@"<Version>.*?</Version>");
            if (versionRegex.IsMatch(projectFile))
            {
                // Update existing Version element
                projectFile = versionRegex.Replace(projectFile, $"<Version>{Version}</Version>");
            }
            else
            {
                // Add Version element after PropertyGroup opening tag
                var propertyGroupRegex = new Regex(@"<PropertyGroup>");
                projectFile = propertyGroupRegex.Replace(projectFile, $"<PropertyGroup>\r\n    <Version>{Version}</Version>", 1);
            }

            File.WriteAllText(ProjectPath, projectFile);
            Log.LogMessage(MessageImportance.Normal, $"Updated project file with version {Version}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to update project file: {ex.Message}");
        }
    }

    /// <summary>
    /// Calculates a short hash from a commit
    /// </summary>
    private string CalculateCommitShortHash(Commit commit)
    {
        if (commit == null) return "0000000";
        return commit.Sha.Substring(0, 7);
    }

    /// <summary>
    /// Extracts version from release branch name
    /// </summary>
    internal SemVer ExtractReleaseVersion(string branchName)
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

        if (versionPart.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            versionPart = versionPart.Substring(TagPrefix.Length);
        }

        return ParseSemVer(versionPart);
    }

    /// <summary>
    /// Parses a string into a semantic version
    /// </summary>
    internal SemVer ParseSemVer(string version)
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

    /// <summary>
    /// Log commit count and history since a specific tag
    /// </summary>
    private void LogCommitsSinceTag(Repository repo, Commit tagCommit)
    {
        try
        {
            Log.LogMessage(MessageImportance.High, "--- DEBUG: Commit Information ---");

            // Count commits since tag
            int commitCount = 0;
            var filter = new CommitFilter
            {
                ExcludeReachableFrom = tagCommit,
                IncludeReachableFrom = repo.Head.Tip
            };

            var commits = repo.Commits.QueryBy(filter).ToList();
            commitCount = commits.Count;

            Log.LogMessage(MessageImportance.High, $"Previous tag commit: {tagCommit.Sha.Substring(0, 7)} from {tagCommit.Author.When.ToString("yyyy-MM-dd HH:mm:ss")}");
            Log.LogMessage(MessageImportance.High, $"Commit message: {tagCommit.MessageShort}");
            Log.LogMessage(MessageImportance.High, $"Number of commits since tag: {commitCount}");

            // Show recent commits
            if (ExtraDebug && commitCount > 0)
            {
                Log.LogMessage(MessageImportance.High, "Recent commits since tag:");
                foreach (var commit in commits.Take(Math.Min(5, commitCount)))
                {
                    Log.LogMessage(MessageImportance.High, $"  {commit.Sha.Substring(0, 7)} - {commit.MessageShort} ({commit.Author.Name}, {commit.Author.When.ToString("yyyy-MM-dd")})");
                }
            }

            Log.LogMessage(MessageImportance.High, "--- END DEBUG ---");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to get commit history: {ex.Message}");
        }
    }

    /// <summary>
    /// Log the dependency graph for this project
    /// </summary>
    private void LogDependencyGraph()
    {
        try
        {
            Log.LogMessage(MessageImportance.High, "--- DEBUG: Project Dependency Graph ---");
            Log.LogMessage(MessageImportance.High, $"Project: {Path.GetFileName(ProjectPath)}");

            if (Dependencies != null && Dependencies.Length > 0)
            {
                Log.LogMessage(MessageImportance.High, "Dependencies:");
                foreach (var dependency in Dependencies)
                {
                    var dependencyPath = dependency.ItemSpec;
#if NET472
                    var relativeDependencyPath = NormalizePath(PathUtils.GetRelativePath(RepoRoot, dependencyPath));
#else
                    var relativeDependencyPath = NormalizePath(Path.GetRelativePath(RepoRoot, dependencyPath));
#endif
                    Log.LogMessage(MessageImportance.High, $"  - {Path.GetFileNameWithoutExtension(dependencyPath)} ({relativeDependencyPath})");
                }
            }
            else
            {
                Log.LogMessage(MessageImportance.High, "No dependencies detected");
            }

            Log.LogMessage(MessageImportance.High, "--- END DEBUG ---");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to log dependency graph: {ex.Message}");
        }
    }

    // Add these methods to the MonoRepoVersionTask class

    /// <summary>
    /// Logs all files in the project path at the current HEAD commit
    /// </summary>
    private void LogFilesInProjectPath(Repository repo, string projectPath)
    {
        try
        {
            Log.LogMessage(MessageImportance.High, "--- DEBUG: Files in Project Path ---");
            Log.LogMessage(MessageImportance.High, $"Project path: {projectPath}");

            // Get the commit tree from HEAD
            var headCommit = repo.Head.Tip;
            var tree = headCommit.Tree;

            // Find all entries that match the project path
            var projectEntries = tree
                .Where(e => e.Path.StartsWith(projectPath))
                .OrderBy(e => e.Path)
                .ToList();

            Log.LogMessage(MessageImportance.High, $"Found {projectEntries.Count} files in project path");

            // Group files by directory for better readability
            var filesByDirectory = projectEntries
                .GroupBy(e => Path.GetDirectoryName(e.Path) ?? string.Empty)
                .OrderBy(g => g.Key);

            foreach (var dirGroup in filesByDirectory)
            {
                var dirPath = dirGroup.Key;
                Log.LogMessage(MessageImportance.High, $"Directory: {(string.IsNullOrEmpty(dirPath) ? projectPath : dirPath)}");

                foreach (var entry in dirGroup.Take(Math.Min(dirGroup.Count(), 10)))
                {
                    var fileName = Path.GetFileName(entry.Path);
                    Log.LogMessage(MessageImportance.High, $"  - {fileName} ({entry.Mode})");
                }

                if (dirGroup.Count() > 10)
                {
                    Log.LogMessage(MessageImportance.High, $"  ... and {dirGroup.Count() - 10} more files");
                }
            }

            Log.LogMessage(MessageImportance.High, "--- END DEBUG ---");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to log files in project path: {ex.Message}");
        }
    }

    /// <summary>
    /// Analyze what types of files were changed (language, role, etc.)
    /// </summary>
    private void AnalyzeChangedFiles(Repository repo, Commit tagCommit, string projectPath)
    {
        try
        {
            if (tagCommit == null)
                return;

            Log.LogMessage(MessageImportance.High, "--- DEBUG: Changed File Analysis ---");

            var compareOptions = new CompareOptions();
            var diff = repo.Diff.Compare<TreeChanges>(
                tagCommit.Tree,
                repo.Head.Tip.Tree,
                compareOptions);

            var projectChanges = diff.Where(c => c.Path.StartsWith(projectPath)).ToList();

            // Group by file extension
            var filesByExt = projectChanges
                .GroupBy(c => Path.GetExtension(c.Path).ToLowerInvariant())
                .OrderByDescending(g => g.Count());

            Log.LogMessage(MessageImportance.High, "Changes by file type:");
            foreach (var extGroup in filesByExt)
            {
                var ext = string.IsNullOrEmpty(extGroup.Key) ? "(no extension)" : extGroup.Key;
                Log.LogMessage(MessageImportance.High, $"  {ext}: {extGroup.Count()} files");
            }

            // Check for common important files
            var hasProjectFileChanges = projectChanges.Any(c =>
                c.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                c.Path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                c.Path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase));

            var hasPackageChanges = projectChanges.Any(c =>
                c.Path.EndsWith("packages.config", StringComparison.OrdinalIgnoreCase) ||
                c.Path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase) ||
                c.Path.EndsWith("nuget.config", StringComparison.OrdinalIgnoreCase));

            var hasConfigChanges = projectChanges.Any(c =>
                c.Path.EndsWith(".config", StringComparison.OrdinalIgnoreCase) ||
                c.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !c.Path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase));

            if (hasProjectFileChanges)
                Log.LogMessage(MessageImportance.High, "  - Project file changes detected");

            if (hasPackageChanges)
                Log.LogMessage(MessageImportance.High, "  - Package dependency changes detected");

            if (hasConfigChanges)
                Log.LogMessage(MessageImportance.High, "  - Configuration file changes detected");

            Log.LogMessage(MessageImportance.High, "--- END DEBUG ---");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to analyze changed files: {ex.Message}");
        }
    }

    /// <summary>
    /// Enhanced method to check for changes since the given commit, with improved dependency change detection
    /// </summary>
    private bool ProjectHasChangedSinceTag(Repository repo, Commit tagCommit, string projectPath)
    {
        if (tagCommit == null)
            return true;

        try
        {
            // Get the files changed between the tag commit and HEAD
            var compareOptions = new CompareOptions();

            var diff = repo.Diff.Compare<TreeChanges>(
                tagCommit.Tree,
                repo.Head.Tip.Tree,
                compareOptions);

            // Important: Normalize slashes in paths for consistent comparison
            string normalizedProjectPath = NormalizePath(projectPath);

            // Look for changes in the project directory
            bool hasChanges = diff.Any(c => NormalizePath(c.Path).StartsWith(normalizedProjectPath));

            // Track which changes triggered the version update for better debugging
            string versionChangeReason = null;

            if (Debug)
            {
                Log.LogMessage(MessageImportance.High, $"Checking for changes in {normalizedProjectPath} since commit {tagCommit.Sha.Substring(0, 7)}");
                Log.LogMessage(MessageImportance.High, $"Checking path: {normalizedProjectPath}");

                if (hasChanges)
                {
                    var changedFiles = diff.Where(c => NormalizePath(c.Path).StartsWith(normalizedProjectPath))
                        .Take(5)
                        .Select(c => $"{c.Path} ({c.Status})");
                    Log.LogMessage(MessageImportance.High, $"Changes detected in: {string.Join(", ", changedFiles)}");
                    versionChangeReason = $"Project files changed: {string.Join(", ", changedFiles.Take(3))}";

                    // Show total number of changes in this path
                    int totalChangesInPath = diff.Count(c => NormalizePath(c.Path).StartsWith(normalizedProjectPath));
                    if (totalChangesInPath > 5)
                    {
                        Log.LogMessage(MessageImportance.High, $"... and {totalChangesInPath - 5} more files changed");
                    }
                }
                else
                {
                    Log.LogMessage(MessageImportance.High, $"No changes detected in project path");
                }
            }

            // Check for changes in dependencies
            if (!hasChanges && Dependencies != null)
            {
                // First, check direct changes to dependency files
                foreach (var dependency in Dependencies)
                {
                    var dependencyPath = dependency.ItemSpec;
#if NET472
                    var relativeDependencyPath = NormalizePath(PathUtils.GetRelativePath(RepoRoot, dependencyPath));
#else
                    var relativeDependencyPath = NormalizePath(Path.GetRelativePath(RepoRoot, dependencyPath));
#endif
                    string normalizedDependencyPath = NormalizePath(relativeDependencyPath);
                    string dependencyDirectory = Path.GetDirectoryName(normalizedDependencyPath);
                    if (string.IsNullOrEmpty(dependencyDirectory))
                        dependencyDirectory = normalizedDependencyPath;

                    // Check if any changes exist in the dependency's directory
                    var dependencyChanges = diff.Where(c => NormalizePath(c.Path).StartsWith(dependencyDirectory)).ToList();
                    if (dependencyChanges.Any())
                    {
                        if (Debug)
                        {
                            var changedFiles = dependencyChanges.Take(3).Select(c => c.Path);
                            Log.LogMessage(MessageImportance.High, $"Detected changes in dependency: {normalizedDependencyPath}");
                            Log.LogMessage(MessageImportance.High, $"Changed files: {string.Join(", ", changedFiles)}");
                        }
                        else
                        {
                            Log.LogMessage(MessageImportance.Normal, $"Detected changes in dependency: {normalizedDependencyPath}");
                        }

                        hasChanges = true;
                        versionChangeReason = $"Dependency changed: {normalizedDependencyPath}";
                        break;
                    }

                    // Check for project-specific tag changes (to detect when a dependency was versioned)
                    string dependencyName = Path.GetFileNameWithoutExtension(dependencyPath);
                    try
                    {
                        var dependencyTag = GetMostRecentProjectTag(repo, dependencyName);
                        if (dependencyTag != null && dependencyTag.Commit != null &&
                            dependencyTag.Commit.Sha != tagCommit.Sha &&
                            IsCommitNewerThan(repo, dependencyTag.Commit, tagCommit))
                        {
                            if (Debug)
                            {
                                Log.LogMessage(MessageImportance.High, $"Detected version change in dependency: {dependencyName}");
                                Log.LogMessage(MessageImportance.High, $"Dependency was versioned from {tagCommit.Sha.Substring(0, 7)} to {dependencyTag.Commit.Sha.Substring(0, 7)}");
                                Log.LogMessage(MessageImportance.High, $"Dependency version is now {dependencyTag.SemVer.Major}.{dependencyTag.SemVer.Minor}.{dependencyTag.SemVer.Patch}");
                            }
                            else
                            {
                                Log.LogMessage(MessageImportance.Normal, $"Detected version change in dependency: {dependencyName}");
                            }

                            hasChanges = true;
                            versionChangeReason = $"Dependency {dependencyName} was versioned";
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"Error checking dependency version: {ex.Message}");
                    }

                    if (Debug)
                    {
                        Log.LogMessage(MessageImportance.Normal, $"No changes detected in dependency: {normalizedDependencyPath}");
                    }
                }
            }

            // Check specifically for packages.lock.json changes
            if (!hasChanges)
            {
                // Get the project directory path
                string projectDir = Path.GetDirectoryName(projectPath);

                // Create the packages.lock.json path with normalized slashes
                string lockFileName = "packages.lock.json";
                string lockFilePath = projectDir == "" ? lockFileName : Path.Combine(projectDir, lockFileName);
                string normalizedLockFilePath = NormalizePath(lockFilePath);

                // Check all changes for any packages.lock.json files that match our project path
                foreach (var change in diff)
                {
                    string normalizedChangePath = NormalizePath(change.Path);

                    // Check if this is our project's packages.lock.json
                    if (normalizedChangePath.Equals(normalizedLockFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Debug)
                        {
                            Log.LogMessage(MessageImportance.High, $"Detected changes in package lock file: {change.Path}");

                            // Attempt to show detailed package changes
                            TryShowPackageLockChanges(repo, change.Path, tagCommit);
                        }
                        else
                        {
                            Log.LogMessage(MessageImportance.Normal, $"Detected changes in package lock file: {change.Path}");
                        }

                        hasChanges = true;
                        versionChangeReason = $"Package dependencies changed in lock file";
                        break;
                    }
                }
            }

            // Log the reason for version change if any
            if (hasChanges && Debug && versionChangeReason != null)
            {
                Log.LogMessage(MessageImportance.High, $"Version change triggered by: {versionChangeReason}");
            }

            return hasChanges;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Error checking for changes: {ex.Message}");
            // If we can't determine changes, assume there are changes
            return true;
        }
    }

    /// <summary>
    /// Gets the most recent project-specific tag for a project
    /// </summary>
    private VersionTag GetMostRecentProjectTag(Repository repo, string projectName)
    {
        var projectSuffix = $"-{projectName.ToLowerInvariant()}";

        // Get all project-specific version tags
        var projectVersionTags = repo.Tags
            .Where(t => t.FriendlyName.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.FriendlyName.ToLowerInvariant().EndsWith(projectSuffix))
            .Select(t =>
            {
                var tagName = t.FriendlyName;
                var versionPart = tagName.Substring(TagPrefix.Length, tagName.Length - TagPrefix.Length - projectSuffix.Length);

                return new VersionTag
                {
                    Tag = t,
                    SemVer = ParseSemVer(versionPart),
                    Commit = t.Target as Commit,
                    IsGlobal = false
                };
            })
            .Where(vt => vt.SemVer != null)
            .OrderByDescending(vt => vt.SemVer.Major)
            .ThenByDescending(vt => vt.SemVer.Minor)
            .ThenByDescending(vt => vt.SemVer.Patch)
            .ToList();

        // Return the most recent project-specific version tag
        if (projectVersionTags.Any())
        {
            return projectVersionTags.First();
        }

        return null;
    }

    /// <summary>
    /// Checks if commit A is newer than commit B
    /// </summary>
    private bool IsCommitNewerThan(Repository repo, Commit commitA, Commit commitB)
    {
        if (commitA == null || commitB == null)
            return false;

        try
        {
            // Check if commit A is in the history of HEAD but commit B is not
            var filter = new CommitFilter
            {
                ExcludeReachableFrom = commitB,
                IncludeReachableFrom = commitA
            };

            return repo.Commits.QueryBy(filter).Any();
        }
        catch
        {
            // If we can't determine, assume it's not newer
            return false;
        }
    }
    /// <summary>
    /// Normalize path separators to forward slashes for consistent comparison
    /// </summary>
    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        // Replace backslashes with forward slashes for consistent comparison
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// Try to show package changes in packages.lock.json
    /// </summary>
    private void TryShowPackageLockChanges(Repository repo, string lockFilePath, Commit tagCommit)
    {
        try
        {
            // Get the old version of the file
            string oldContent = null;

            var oldBlob = tagCommit[lockFilePath]?.Target as Blob;
            if (oldBlob != null)
            {
                using (var stream = oldBlob.GetContentStream())
                using (var reader = new StreamReader(stream))
                {
                    oldContent = reader.ReadToEnd();
                }
            }

            // Get the current version of the file
            string newContent = null;
            var fullPath = Path.Combine(RepoRoot, lockFilePath);
            if (File.Exists(fullPath))
            {
                newContent = File.ReadAllText(fullPath);
            }

            if (oldContent != null && newContent != null)
            {
                Log.LogMessage(MessageImportance.High, "  Package lock changes:");

                // Extract package versions - this is a simplified approach
                // A full implementation would use proper JSON parsing
                var packageVersionRegex = new Regex(@"""([^""]+)"":[\s\n]*{[\s\n]*""type"":[\s\n]*""[^""]*"",[\s\n]*""requested"":[\s\n]*""[^""]*"",[\s\n]*""resolved"":[\s\n]*""([^""]+)""", RegexOptions.IgnoreCase);

                var oldMatches = packageVersionRegex.Matches(oldContent);
                var newMatches = packageVersionRegex.Matches(newContent);

                var oldVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var newVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match match in oldMatches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string package = match.Groups[1].Value;
                        string version = match.Groups[2].Value;
                        oldVersions[package] = version;
                    }
                }

                foreach (Match match in newMatches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string package = match.Groups[1].Value;
                        string version = match.Groups[2].Value;
                        newVersions[package] = version;
                    }
                }

                // Find changes
                var changes = new List<string>();

                // Look for version changes and additions
                foreach (var kvp in newVersions)
                {
                    if (oldVersions.TryGetValue(kvp.Key, out string oldVersion))
                    {
                        if (oldVersion != kvp.Value)
                        {
                            changes.Add($"    - {kvp.Key}: {oldVersion} → {kvp.Value}");
                        }
                    }
                    else
                    {
                        changes.Add($"    - {kvp.Key}: (added) {kvp.Value}");
                    }
                }

                // Look for removals
                foreach (var kvp in oldVersions)
                {
                    if (!newVersions.ContainsKey(kvp.Key))
                    {
                        changes.Add($"    - {kvp.Key}: {kvp.Value} (removed)");
                    }
                }

                // Display changes
                if (changes.Count > 0)
                {
                    foreach (var change in changes)
                    {
                        Log.LogMessage(MessageImportance.High, change);
                    }
                }
                else
                {
                    Log.LogMessage(MessageImportance.High, "    No package version changes detected (structural changes only)");
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogMessage(MessageImportance.High, $"    Could not analyze package lock changes: {ex.Message}");
        }
    }

    /// <summary>
    /// Enhanced method to log change tree details with focus on packages.lock.json
    /// </summary>
    private void LogChangeTreeDetails(Repository repo, Commit tagCommit, string projectPath)
    {
        try
        {
            if (tagCommit == null)
            {
                Log.LogMessage(MessageImportance.High, "Cannot log change tree: No base commit available");
                return;
            }

            // Normalize the project path
            string normalizedProjectPath = NormalizePath(projectPath);

            Log.LogMessage(MessageImportance.High, "--- DEBUG: Change Tree Details ---");
            Log.LogMessage(MessageImportance.High, $"Comparing changes between commit {tagCommit.Sha.Substring(0, 7)} and HEAD");

            var compareOptions = new CompareOptions();
            var diff = repo.Diff.Compare<TreeChanges>(
                tagCommit.Tree,
                repo.Head.Tip.Tree,
                compareOptions);

            var allChanges = diff.ToList();
            Log.LogMessage(MessageImportance.High, $"Total changed files: {allChanges.Count}");

            // Group changes by type
            var addedFiles = allChanges.Where(c => c.Status == ChangeKind.Added).ToList();
            var modifiedFiles = allChanges.Where(c => c.Status == ChangeKind.Modified).ToList();
            var deletedFiles = allChanges.Where(c => c.Status == ChangeKind.Deleted).ToList();
            var renamedFiles = allChanges.Where(c => c.Status == ChangeKind.Renamed).ToList();

            // Report overall statistics
            Log.LogMessage(MessageImportance.High, $"Change summary: {addedFiles.Count} added, {modifiedFiles.Count} modified, {deletedFiles.Count} deleted, {renamedFiles.Count} renamed");

            // Show project-specific changes
            var projectChanges = allChanges.Where(c => NormalizePath(c.Path).StartsWith(normalizedProjectPath)).ToList();
            Log.LogMessage(MessageImportance.High, $"Changes in project path '{normalizedProjectPath}': {projectChanges.Count} files");

            if (projectChanges.Count > 0)
            {
                Log.LogMessage(MessageImportance.High, "Files changed in project:");
                foreach (var change in projectChanges.Take(15))
                {
                    var fileName = Path.GetFileName(change.Path);
                    Log.LogMessage(MessageImportance.High, $"  - {fileName} ({change.Status})");
                }

                if (projectChanges.Count > 15)
                {
                    Log.LogMessage(MessageImportance.High, $"  ... and {projectChanges.Count - 15} more files");
                }
            }

            // Check for package lock changes
            var packageLockChanges = allChanges
                .Where(c => Path.GetFileName(c.Path).Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (packageLockChanges.Any())
            {
                Log.LogMessage(MessageImportance.High, "Package lock file changes detected:");
                foreach (var change in packageLockChanges)
                {
                    Log.LogMessage(MessageImportance.High, $"  - {change.Path} ({change.Status})");

                    // Check if this packages.lock.json change affects our project
                    string normalizedChangePath = NormalizePath(change.Path);
                    string projectDir = NormalizePath(Path.GetDirectoryName(projectPath));

                    if (normalizedChangePath.StartsWith(projectDir) || (projectDir == "" && normalizedChangePath == "packages.lock.json"))
                    {
                        Log.LogMessage(MessageImportance.High, $"  This package lock change affects the current project");

                        // Show detailed package changes
                        TryShowPackageLockChanges(repo, change.Path, tagCommit);
                    }
                }
            }

            // Show dependency changes if any
            if (Dependencies != null && Dependencies.Length > 0)
            {
                Log.LogMessage(MessageImportance.High, "Changes in dependencies:");

                foreach (var dependency in Dependencies)
                {
                    var dependencyPath = dependency.ItemSpec;
#if NET472
                    var relativeDependencyPath = NormalizePath(PathUtils.GetRelativePath(RepoRoot, dependencyPath));
#else
                    var relativeDependencyPath = NormalizePath(Path.GetRelativePath(RepoRoot, dependencyPath));
#endif
                    string normalizedDependencyPath = NormalizePath(relativeDependencyPath);

                    var depChanges = allChanges.Where(c => NormalizePath(c.Path).StartsWith(normalizedDependencyPath)).ToList();

                    Log.LogMessage(MessageImportance.High, $"  Dependency: {Path.GetFileNameWithoutExtension(dependencyPath)} ({normalizedDependencyPath})");
                    Log.LogMessage(MessageImportance.High, $"  Changed files: {depChanges.Count}");

                    if (depChanges.Count > 0)
                    {
                        foreach (var change in depChanges.Take(5))
                        {
                            var fileName = Path.GetFileName(change.Path);
                            Log.LogMessage(MessageImportance.High, $"    - {fileName} ({change.Status})");
                        }

                        if (depChanges.Count > 5)
                        {
                            Log.LogMessage(MessageImportance.High, $"    ... and {depChanges.Count - 5} more files");
                        }
                    }
                }
            }

            Log.LogMessage(MessageImportance.High, "--- END DEBUG ---");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to log change tree details: {ex.Message}");
        }
    }

    /// <summary>
    /// Branch types
    /// </summary>
    internal enum BranchType
    {
        Main,
        Release,
        Feature
    }

    /// <summary>
    /// Semantic version class
    /// </summary>
    internal class SemVer
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
    }

    /// <summary>
    /// Version tag class that combines tag information with parsed semantic version
    /// </summary>
    private class VersionTag
    {
        public Tag Tag { get; set; }
        public SemVer SemVer { get; set; }
        public Commit Commit { get; set; }
        public bool IsGlobal { get; set; }
    }
}