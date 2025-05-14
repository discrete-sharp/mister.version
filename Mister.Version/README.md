# Mister.Version

![version](https://img.shields.io/badge/version-1.0.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

A sophisticated automatic versioning system for .NET monorepos built on MSBuild. Mister.Version (MR Version → MonoRepo Version) provides intelligent, change-based versioning that increases version numbers only when actual changes are detected in a project or its dependencies.

## Features

- **Change-Based Versioning**: Version numbers only increment when actual code changes are detected
- **Dependency-Aware**: Automatically bumps versions when dependencies change
- **Branch-Aware**: Different versioning strategies for main, release, and feature branches
- **Package Lock Detection**: Detects changes in dependencies via packages.lock.json
- **Project Type Filtering**: Skip versioning for test projects and non-packable projects
- **MSBuild Integration**: Seamlessly integrates with your build process
- **Zero-Commit Approach**: No need to commit version changes to your repo
- **Customizable**: Extensive configuration options

## How It Works

MonoRepo Versioning uses Git history to intelligently determine when to increment versions. At build time, it:

1. Identifies the current branch type (main, release, feature)
2. Determines the base version from tags and branch context
3. Checks for changes in the project and its dependencies
4. Applies appropriate versioning rules based on context
5. Injects the calculated version into MSBuild properties

The tool follows these versioning rules:

- **Main Branch**: Commits to main increment the patch version (8.2.0 → 8.2.1)
- **Release Branches**: Patch increments for changes in the branch (7.3.0 → 7.3.2)
- **Feature Branches**: Feature branches use base version + branch name (8.2.0-feature-name.abcdef1)

## Getting Started

### Prerequisites

- .NET SDK 6.0+
- Git installed and accessible in PATH
- A .NET solution using MSBuild

### Installation

#### Option 1: NuGet Package

```bash
dotnet add package Mister.Version --version 1.0.0
```

#### Option 2: Manual Installation

1. Build the `MonoRepo.Versioning` project
2. Copy the output DLL and targets files to your solution's build directory

### Basic Setup

1. Add the targets file reference to each project that should use automatic versioning:

```xml
<Import Project="$(MSBuildThisFileDirectory)..\..\build\MonoRepo.Versioning.targets" />
```

2. Create an initial version tag for your repository:

```bash
git tag v1.0.0
```

3. Build your solution:

```bash
dotnet build
```

The tool will automatically calculate and apply versions to your assemblies and packages.

## Configuration

### MSBuild Properties

| Property | Description | Default |
|----------|-------------|---------|
| `MonoRepoVersionEnabled` | Enable/disable automatic versioning | `true` |
| `MonoRepoVersionRepoRoot` | Path to the Git repository root | Auto-detected |
| `MonoRepoVersionTagPrefix` | Prefix for version tags | `v` |
| `MonoRepoVersionUpdateProjectFile` | Update project files with versions | `false` |
| `MonoRepoVersionDebug` | Enable debug logging | `false` |
| `MonoRepoVersionExtraDebug` | Enable extra debug logging | `false` |
| `MonoRepoVersionSkipTestProjects` | Skip versioning for test projects | `true` |
| `MonoRepoVersionSkipNonPackableProjects` | Skip versioning for non-packable projects | `true` |
| `MonoRepoVersionForce` | Force a specific version | Empty |

You can set these properties in your csproj file:

```xml
<PropertyGroup>
  <MonoRepoVersionDebug>true</MonoRepoVersionDebug>
  <MonoRepoVersionTagPrefix>version-</MonoRepoVersionTagPrefix>
</PropertyGroup>
```

Or pass them via command line:

```bash
dotnet build /p:MonoRepoVersionDebug=true
```

### Project-Specific Settings

To exclude a project from automatic versioning, set:

```xml
<PropertyGroup>
  <MonoRepoVersionEnabled>false</MonoRepoVersionEnabled>
</PropertyGroup>
```

Test projects are detected automatically via:

```xml
<PropertyGroup>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

Non-packable projects are detected via:

```xml
<PropertyGroup>
  <IsPackable>false</IsPackable>
</PropertyGroup>
```

## Version Tagging

The tool also supports creating Git tags for versions:

```bash
# Create version tags
dotnet msbuild -t:MonoRepoVersionTag

# Create and push tags
dotnet msbuild -t:MonoRepoVersionPushTags
```

Tags are only created for projects that have detected changes.

## Advanced Topics

### Global vs. Project-Specific Tags

MonoRepo Versioning supports two types of tags:

1. **Global tags**: Apply to the entire repository (e.g., `v8.2.0`)
2. **Project-specific tags**: Apply to specific projects (e.g., `v8.2.1-myproject`)

The system intelligently combines these to determine the appropriate base version for each project.

### Selective Feature Branch Versioning

On feature branches, only projects with actual changes get the feature branch version suffix. Unchanged projects maintain their normal version number.

### Dependency Graph Analysis

When a project's dependency changes, the project itself will also get a version bump. This ensures proper semantic versioning across your monorepo.

## Debugging and Troubleshooting

Enable debug logging to see detailed information about the versioning process:

```bash
dotnet build /p:MonoRepoVersionDebug=true -v:n
```

For even more details, enable extra debug logging:

```bash
dotnet build /p:MonoRepoVersionExtraDebug=true -v:n
```

This will display:
- Project dependency graphs
- Files inspected for changes
- Detailed change analysis
- Tag detection results
- Commit history information

### Common Issues

1. **Versions not incrementing**: Check that changes were detected in the project files
2. **Wrong version on feature branch**: Verify branch naming and check for changes
3. **Package has wrong version**: Ensure MSBuild properties are correctly set

## Implementation Details

### MSBuild Task

The core of the system is the `Mister.Version.VersionTask` MSBuild task that:

- Detects the repository state and branch type
- Examines Git history for changes
- Implements the versioning rules
- Outputs version information to MSBuild properties

### Change Detection

The system uses LibGit2Sharp to detect changes between tags. It examines:

1. Direct project file changes
2. Changes in project dependencies
3. Changes in package dependencies
4. Version changes in dependencies

### Path Handling

The tool carefully normalizes paths to handle cross-platform path separators correctly:

```csharp
private string NormalizePath(string path)
{
    if (string.IsNullOrEmpty(path))
        return string.Empty;
        
    // Replace backslashes with forward slashes for consistent comparison
    return path.Replace('\\', '/');
}
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.