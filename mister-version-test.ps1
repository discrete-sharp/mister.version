# Run a test report of initial state
    Write-Host "Running initial version report..." -ForegroundColor Magenta
    # Uncomment to run actual tool
    # & $toolPath report -r $repoPath -o text
    Write-Host "[Simulated report] All projects at version 1.0.0" -ForegroundColor Gray# Mister.Version Test Script
# This script creates a mock monorepo, sets up projects, simulates changes, and tests versioning

# Configuration
$repoName = "test-monorepo"
$repoPath = Join-Path $PSScriptRoot $repoName
$toolPath = "mr-version" # Assumes the tool is installed globally
$defaultVersion = "1.0.0"
$tag1 = "v1.0.0"
$tag2 = "v2.0.0"
$tag3 = "v3.0.0"

# Project structure for our test repo
$projects = @(
    @{
        Name = "Core";
        Path = "src/Core";
        Dependencies = @();
        Files = @("CoreServices.cs", "CoreModels.cs")
    },
    @{
        Name = "Data";
        Path = "src/Data";
        Dependencies = @("Core");
        Files = @("DataRepository.cs", "DataModels.cs")
    },
    @{
        Name = "Api";
        Path = "src/Api";
        Dependencies = @("Core", "Data");
        Files = @("ApiController.cs", "Startup.cs")
    },
    @{
        Name = "UI";
        Path = "src/UI";
        Dependencies = @("Core", "Api");
        Files = @("UIComponents.cs", "UIModels.cs")
    },
    @{
        Name = "Tests";
        Path = "tests/Core.Tests";
        Dependencies = @("Core");
        Files = @("CoreTests.cs");
        IsTest = $true
    }
)

# Ensure PowerShell has access to Git
if (-not (Get-Command "git" -ErrorAction SilentlyContinue)) {
    Write-Error "Git is not installed or not in the PATH. Please install Git and try again."
    exit 1
}

# Clean up existing test repo if it exists
if (Test-Path $repoPath) {
    Write-Host "Cleaning up existing test repo at $repoPath..." -ForegroundColor Yellow
    Remove-Item -Path $repoPath -Recurse -Force
}

# Create the test directory
New-Item -Path $repoPath -ItemType Directory | Out-Null
Push-Location $repoPath

try {
    # Initialize Git repo
    Write-Host "Initializing Git repository..." -ForegroundColor Cyan
    git init
    git config user.name "Test User"
    git config user.email "test@example.com"

    # Initial commit to set up repo
    New-Item -Path "README.md" -ItemType File -Value "# Test MonoRepo`nThis is a test monorepo for versioning." | Out-Null
    git add README.md
    git commit -m "Initial commit"

    # Create the project structure
    Write-Host "Creating project structure..." -ForegroundColor Cyan
    foreach ($project in $projects) {
        $projectPath = Join-Path $repoPath $project.Path
        New-Item -Path $projectPath -ItemType Directory -Force | Out-Null
        
        # Create .csproj file
        $csprojContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Version>$defaultVersion</Version>
"@

        if ($project.IsTest) {
            $csprojContent += @"
    <IsTestProject>true</IsTestProject>
"@
        }

        $csprojContent += @"
  </PropertyGroup>

"@

        # Add dependencies
        if ($project.Dependencies.Count -gt 0) {
            $csprojContent += @"
  <ItemGroup>
"@

            foreach ($dep in $project.Dependencies) {
                $depPath = ($projects | Where-Object { $_.Name -eq $dep }).Path
                $relativePath = "..\..\" + $depPath + "\" + $dep + ".csproj"
                
                $csprojContent += @"
    <ProjectReference Include="$relativePath" />
"@
            }

            $csprojContent += @"
  </ItemGroup>

"@
        }

        $csprojContent += @"
  <!-- Import the Mister.Version targets -->
  <Import Project="$(MSBuildThisFileDirectory)..\..\..\build\Mister.Version.targets" />
</Project>
"@

        $csprojPath = Join-Path $projectPath ($project.Name + ".csproj")
        Set-Content -Path $csprojPath -Value $csprojContent
        
        # Create source files
        foreach ($file in $project.Files) {
            $fileContent = @"
namespace $($project.Name)
{
    public class $([System.IO.Path]::GetFileNameWithoutExtension($file))
    {
        // Initial version
        public string Version => "v1";
    }
}
"@
            $filePath = Join-Path $projectPath $file
            Set-Content -Path $filePath -Value $fileContent
        }
    }

    # Create build directory with targets file
    $buildDir = Join-Path $repoPath "build"
    New-Item -Path $buildDir -ItemType Directory -Force | Out-Null
    
    $targetsContent = @"
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <!-- Define properties -->
  <PropertyGroup>
    <MrVersionEnabled Condition="'`$(MrVersionEnabled)' == ''">true</MrVersionEnabled>
    <MrVersionRepoRoot Condition="'`$(MrVersionRepoRoot)' == ''">$(MSBuildThisFileDirectory)../</MrVersionRepoRoot>
    <MrVersionTagPrefix Condition="'`$(MrVersionTagPrefix)' == ''">v</MrVersionTagPrefix>
    <MrVersionUpdateProjectFile Condition="'`$(MrVersionUpdateProjectFile)' == ''">false</MrVersionUpdateProjectFile>
    <MrVersionDebug Condition="'`$(MrVersionDebug)' == ''">true</MrVersionDebug>
    <MrVersionSkipTestProjects Condition="'`$(MrVersionSkipTestProjects)' == ''">true</MrVersionSkipTestProjects>
    <MrVersionSkipNonPackableProjects Condition="'`$(MrVersionSkipNonPackableProjects)' == ''">true</MrVersionSkipNonPackableProjects>
  </PropertyGroup>

  <!-- 
  In a real setup, this would import the actual Mister.Version.VersionTask.
  For testing purposes, we'll just simulate what it would do.
  -->
  <Target Name="_MrVersionCalculate">
    <PropertyGroup>
      <Version>1.0.0</Version>
      <PackageVersion>1.0.0</PackageVersion>
      <AssemblyVersion>1.0.0</AssemblyVersion>
      <FileVersion>1.0.0</FileVersion>
      <InformationalVersion>1.0.0</InformationalVersion>
    </PropertyGroup>
    
    <Message Text="Mister.Version: Using version 1.0.0 for `$(MSBuildProjectName)" Importance="High" />
  </Target>
  
  <PropertyGroup>
    <BuildDependsOn>
      _MrVersionCalculate;
      `$(BuildDependsOn)
    </BuildDependsOn>
  </PropertyGroup>
</Project>
"@
    
    $targetsPath = Join-Path $buildDir "Mister.Version.targets"
    Set-Content -Path $targetsPath -Value $targetsContent

    # Initial commit with all projects
    git add --all
    git commit -m "Add initial project structure"

    # Tag initial version
    git tag $tag1
    Write-Host "Created initial tag: $tag1" -ForegroundColor Green

    # Run a test report of initial state
    Write-Host "Running initial version report..." -ForegroundColor Magenta
    # Uncomment to run actual tool
    # & $toolPath report -r $repoPath -o text
    Write-Host "[Simulated report] All projects at version 1.0.0" -ForegroundColor Gray

    # SCENARIO 1: Simple Core Change
    Write-Host "`nSCENARIO 1: Simple Core Change" -ForegroundColor Yellow
    
    # Modify Core project
    $coreModelPath = Join-Path $repoPath "src/Core/CoreModels.cs"
    $coreModelContent = Get-Content $coreModelPath -Raw
    $coreModelContent = $coreModelContent -replace 'public string Version => "v1";', 'public string Version => "v2";'
    Set-Content -Path $coreModelPath -Value $coreModelContent
    
    git add $coreModelPath
    git commit -m "Update Core models to v2"

    # Simulate version checking with and without dependency tracking
    Write-Host "Checking versions after Core change..." -ForegroundColor Magenta
    # & $toolPath report -r $repoPath -o text
    Write-Host "[Simulated report] Core should be at version 1.0.1, others remain at 1.0.0" -ForegroundColor Gray
    
    # Create v2.0.0 tag
    git tag $tag2
    Write-Host "Created new tag: $tag2" -ForegroundColor Green

    # SCENARIO 2: Changes with Dependencies
    Write-Host "`nSCENARIO 2: Changes with Dependencies" -ForegroundColor Yellow
    
    # Create a feature branch
    git checkout -b feature/data-improvements
    
    # Modify Data project
    $dataModelPath = Join-Path $repoPath "src/Data/DataModels.cs"
    $dataModelContent = Get-Content $dataModelPath -Raw
    $dataModelContent = $dataModelContent -replace 'public string Version => "v1";', 'public string Version => "v3";'
    Set-Content -Path $dataModelPath -Value $dataModelContent
    
    git add $dataModelPath
    git commit -m "Update Data models to v3"

    # Simulate version checking on a feature branch
    Write-Host "Checking versions on feature branch..." -ForegroundColor Magenta
    # & $toolPath report -r $repoPath -o text
    Write-Host "[Simulated report] Data should have a feature branch version" -ForegroundColor Gray
    
    # Return to main branch and merge feature
    git checkout master
    git merge feature/data-improvements --no-ff -m "Merge data improvements"

    # Simulate version checking after merge
    Write-Host "Checking versions after feature merge..." -ForegroundColor Magenta
    # & $toolPath report -r $repoPath -o text
    Write-Host "[Simulated report] Data and dependent projects should have new versions" -ForegroundColor Gray

    # SCENARIO 3: Release Branch
    Write-Host "`nSCENARIO 3: Release Branch" -ForegroundColor Yellow
    
    # Create a release branch from v2 tag
    git checkout -b release/v2.0 $tag2
    
    # Fix a bug in Core
    $coreServicePath = Join-Path $repoPath "src/Core/CoreServices.cs"
    $coreServiceContent = Get-Content $coreServicePath -Raw
    $coreServiceContent = $coreServiceContent -replace 'public string Version => "v1";', 'public string Version => "v2.1-hotfix";'
    Set-Content -Path $coreServicePath -Value $coreServiceContent
    
    git add $coreServicePath
    git commit -m "Hotfix for Core services"

    # Simulate version checking on release branch
    Write-Host "Checking versions on release branch..." -ForegroundColor Magenta
    # & $toolPath report -r $repoPath -b release/v2.0 -o text
    Write-Host "[Simulated report] Core should be at version 2.0.1 on release branch" -ForegroundColor Gray

    # SCENARIO 4: Complex Dependency Chain
    Write-Host "`nSCENARIO 4: Complex Dependency Chain" -ForegroundColor Yellow
    
    # Return to main branch
    git checkout master
    
    # Create v3.0.0 tag
    git tag $tag3
    Write-Host "Created new tag: $tag3" -ForegroundColor Green
    
    # Modify UI project
    $uiModelPath = Join-Path $repoPath "src/UI/UIModels.cs"
    $uiModelContent = Get-Content $uiModelPath -Raw
    $uiModelContent = $uiModelContent -replace 'public string Version => "v1";', 'public string Version => "v4";'
    Set-Content -Path $uiModelPath -Value $uiModelContent
    
    git add $uiModelPath
    git commit -m "Update UI models to v4"

    # Now modify Core (which UI depends on indirectly)
    $coreServicePath = Join-Path $repoPath "src/Core/CoreServices.cs"
    $coreServiceContent = Get-Content $coreServicePath -Raw
    $coreServiceContent = $coreServiceContent -replace 'public string Version => "v1";', 'public string Version => "v4";'
    Set-Content -Path $coreServicePath -Value $coreServiceContent
    
    git add $coreServicePath
    git commit -m "Update Core services to v4"

    # Simulate version checking to test dependency chain
    Write-Host "Checking versions after complex dependency changes..." -ForegroundColor Magenta
    # & $toolPath report -r $repoPath -o text
    Write-Host "[Simulated report] Core at 3.0.1, UI at 3.0.1, Api and Data should also change due to dependencies" -ForegroundColor Gray

    # SCENARIO 5: Test Project Changes
    Write-Host "`nSCENARIO 5: Test Project Changes" -ForegroundColor Yellow
    
    # Modify test project
    $testPath = Join-Path $repoPath "tests/Core.Tests/CoreTests.cs"
    $testContent = Get-Content $testPath -Raw
    $testContent = $testContent -replace 'public string Version => "v1";', 'public string Version => "v5-tests";'
    Set-Content -Path $testPath -Value $testContent
    
    git add $testPath
    git commit -m "Update tests to v5"

    # Simulate version checking to verify test projects are excluded
    Write-Host "Checking versions after test changes..." -ForegroundColor Magenta
    # & $toolPath report -r $repoPath -o text
    Write-Host "[Simulated report] Test project changes should not affect versions" -ForegroundColor Gray

    # Final summary
    Write-Host "`nTest Repository Created Successfully:" -ForegroundColor Green
    Write-Host "Location: $repoPath" -ForegroundColor Cyan
    Write-Host "Projects: $($projects.Count)" -ForegroundColor Cyan
    Write-Host "Tags: $tag1, $tag2, $tag3" -ForegroundColor Cyan
    Write-Host "Branches: master, feature/data-improvements, release/v2.0" -ForegroundColor Cyan
    
    # Run the version reporter in different formats
    Write-Host "`nTo test the monorepo versioning tool on this repo, run:" -ForegroundColor Yellow
    Write-Host "  $toolPath report -r $repoPath" -ForegroundColor White
    Write-Host "  $toolPath report -r $repoPath -o json -f report.json" -ForegroundColor White
    Write-Host "  $toolPath report -r $repoPath -b release/v2.0" -ForegroundColor White
    Write-Host "  $toolPath version -r $repoPath -p $repoPath/src/Core/Core.csproj -d" -ForegroundColor White

} catch {
    Write-Error "An error occurred: $_"
    exit 1
} finally {
    # Return to original directory
    Pop-Location
}