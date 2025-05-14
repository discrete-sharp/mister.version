# Mister.Version CLI

![version](https://img.shields.io/badge/version-1.0.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

A powerful command-line tool for analyzing, reporting, and managing versions across your .NET monorepo. Mister.Version CLI works alongside the Mister.Version MSBuild system to provide comprehensive insights into your project versions and dependencies.

## Features

- **Version Reporting**: Generate detailed reports about versions across your monorepo
- **Multiple Output Formats**: Export as text, JSON, or CSV
- **Dependency Analysis**: View complete dependency trees and detect version mismatches
- **Branch-Aware**: Analyze different branches with specific versioning strategies
- **Commit Tracking**: See which commits triggered version changes
- **Project Type Filtering**: Automatically filters test and non-packable projects
- **Customizable**: Extensive command-line options

## Installation

### As a .NET Tool

```bash
# Build and pack the tool
dotnet pack -c Release

# Install globally
dotnet tool install --global --add-source ./bin/Release Mister.Version.CLI

# Or install locally
dotnet tool install --local --add-source ./bin/Release Mister.Version.CLI
```

### From Source

```bash
# Clone the repository
git clone https://github.com/yourusername/mister-version.git

# Build the CLI tool
cd mister-version/src/CLI
dotnet build

# Run directly
dotnet run -- [command] [options]
```

## Commands

The CLI tool provides two main commands:

### 1. Generate Version Report

```bash
mr-version report [options]
```

This command analyzes all projects in your monorepo and generates a comprehensive version report.

#### Options

| Option | Description | Default |
|--------|-------------|---------|
| `-r, --repo <path>` | Repository root path | Current directory |
| `-p, --project-dir <path>` | Projects directory | `src` |
| `-o, --output <format>` | Output format (text, json, csv) | `text` |
| `-f, --file <path>` | Output file path | Console output |
| `-b, --branch <name>` | Branch to analyze | Current branch |
| `-t, --tag-prefix <prefix>` | Tag prefix | `v` |
| `--include-commits` | Include commit information | `true` |
| `--include-dependencies` | Include dependency information | `true` |

#### Example

```bash
# Generate a JSON report for the current repo
mr-version report -o json -f versions.json

# Generate a report for a specific branch
mr-version report -b release/v8.2 -o csv -f release-versions.csv

# Generate a basic report without dependencies or commits
mr-version report --include-dependencies=false --include-commits=false
```

### 2. Calculate Project Version

```bash
mr-version version [options]
```

This command calculates the version for a specific project, showing the exact logic used.

#### Options

| Option | Description | Default |
|--------|-------------|---------|
| `-r, --repo <path>` | Repository root path | Current directory |
| `-p, --project <path>` | Project file path | Required |
| `-t, --tag-prefix <prefix>` | Tag prefix | `v` |
| `-d, --detailed` | Show detailed calculation | `false` |
| `-j, --json` | JSON output | `false` |

#### Example

```bash
# Get detailed version information for a project
mr-version version -p src/Core/Core.csproj -d

# Get version information in JSON format
mr-version version -p src/Api/Api.csproj -j
```

## Report Formats

The tool can generate reports in three formats:

### Text Format (Default)

```
=== MonoRepo Version Report ===
Repository: /path/to/repo
Global Version: 8.2.0
Projects: 5

Project: Core
  Path: src/Core/Core.csproj
  Version: 8.2.1
  Commit: a1b2c3d (2023-05-15)
  Message: Update Core services
  Direct Dependencies (0):
  
Project: Data
  Path: src/Data/Data.csproj
  Version: 8.2.1
  Commit: e5f6g7h (2023-05-14)
  Message: Fix data models
  Direct Dependencies (1):
    - Core (8.2.1)
  
...
```

### JSON Format

```json
{
  "repository": "/path/to/repo",
  "globalVersion": "8.2.0",
  "projectCount": 5,
  "projects": [
    {
      "name": "Core",
      "path": "src/Core/Core.csproj",
      "version": "8.2.1",
      "commit": {
        "sha": "a1b2c3d",
        "date": "2023-05-15",
        "message": "Update Core services"
      },
      "dependencies": {
        "direct": [],
        "all": []
      },
      "isTestProject": false,
      "isPackable": true
    },
    ...
  ]
}
```

### CSV Format

```csv
ProjectName,ProjectPath,Version,CommitSha,CommitDate,CommitMessage,IsTestProject,IsPackable,Dependencies
Core,src/Core/Core.csproj,8.2.1,a1b2c3d,2023-05-15,Update Core services,false,true,
Data,src/Data/Data.csproj,8.2.1,e5f6g7h,2023-05-14,Fix data models,false,true,Core
...
```

## Advanced Usage

### Filtering Projects

The reporter automatically detects and can exclude:

- **Test Projects**: Projects with `<IsTestProject>true</IsTestProject>`
- **Non-Packable Projects**: Projects with `<IsPackable>false</IsPackable>`

### Custom Repository Structure

If your repository has a non-standard structure, you can specify custom paths:

```bash
mr-version report -p path/to/projects -r /custom/repo/location
```

### Analyzing Branches

Compare version information across different branches:

```bash
# Analyze the main branch
mr-version report -b main

# Analyze a release branch
mr-version report -b release/v7.3

# Compare reports
mr-version report -b main -f main.json -o json
mr-version report -b release/v7.3 -f release.json -o json
```

### Dependency Analysis

The tool can analyze the full dependency tree of your projects:

```bash
mr-version report --include-dependencies
```

This will show both direct dependencies and transitive dependencies, helping you understand the ripple effects of version changes.

## Implementation Details

### Project Analysis

The tool uses LibGit2Sharp to analyze the repository, and combines this with project file parsing to:

1. Detect project structure and dependencies
2. Find relevant version tags
3. Analyze commit history
4. Calculate effective versions

### Dependency Detection

Dependencies are detected by parsing `.csproj` files for `ProjectReference` elements:

```csharp
var matches = Regex.Matches(projectContent, @"<ProjectReference Include=""([^""]+)""");
foreach (Match match in matches)
{
    var dependencyPath = match.Groups[1].Value;
    var dependencyName = Path.GetFileNameWithoutExtension(dependencyPath);
    dependencies.Add(dependencyName);
}
```

### Version Tag Analysis

The tool identifies both global and project-specific tags:

```csharp
// Get all project-specific version tags
var projectVersionTags = _repo.Tags
    .Where(t => t.FriendlyName.StartsWith(_options.TagPrefix, StringComparison.OrdinalIgnoreCase))
    .Where(t => t.FriendlyName.ToLowerInvariant().EndsWith(projectSuffix))
    .Select(t => { /* parse tag... */ })
    .OrderByDescending(/* ... */)
    .ToList();
```

## Testing

A comprehensive test script is included to help test the tool. It creates a mock monorepo with:

- Multiple projects with dependencies
- Different branch scenarios
- Version tags
- Simulated changes

Run the test script:

```bash
# PowerShell
.\monorepo-version-test.ps1

# Bash
./monorepo-version-test.sh
```

The script creates a fully functioning test environment with 5 test scenarios:
1. Simple Core Change
2. Changes with Dependencies
3. Release Branch
4. Complex Dependency Chain
5. Test Project Changes

## Common Workflows

### CI/CD Integration

Add version reporting to your CI/CD pipeline:

```yaml
# Azure DevOps example
steps:
- script: |
    dotnet tool install --global MonoRepo.Versioning.CLI
    monorepo-version report -o json -f version-report.json
  displayName: 'Generate Version Report'
  
- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: 'version-report.json'
    artifactName: 'Versions'
```

### Release Process

Before finalizing a release:

1. Run `monorepo-version report` to verify all components have correct versions
2. Check dependency relationships with `--include-dependencies`
3. Create version tags with the main versioning tool

## Troubleshooting

### Common Issues

1. **Tool not finding projects**: Check the `-p/--project-dir` path
2. **Missing version tags**: Ensure your repository has Git tags with the correct prefix
3. **Incorrect dependency tree**: Check that project references are correctly configured

### Debugging

Enable verbose console output:

```bash
mr-version report --debug
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### Development Setup

1. Clone the repository
2. Install required .NET SDK (8.0+)
3. Run `dotnet restore`
4. Run `dotnet build`
5. Run tests with `dotnet test`

## License

This project is licensed under the MIT License - see the LICENSE file for details.