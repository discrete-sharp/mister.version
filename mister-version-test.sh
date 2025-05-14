#!/bin/bash
# Mister.Version Test Script
# This script creates a mock monorepo, sets up projects, simulates changes, and tests versioning

set -e

# Configuration
REPO_NAME="test-monorepo"
REPO_PATH="$(pwd)/$REPO_NAME"
TOOL_PATH="mr-version" # Assumes the tool is installed globally
DEFAULT_VERSION="1.0.0"
TAG1="v1.0.0"
TAG2="v2.0.0"
TAG3="v3.0.0"

# Ensure the script can access Git
if ! command -v git &> /dev/null; then
    echo "Error: Git is not installed or not in the PATH. Please install Git and try again."
    exit 1
fi

# Color codes for output
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
MAGENTA='\033[0;35m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

# Clean up existing test repo if it exists
if [ -d "$REPO_PATH" ]; then
    echo -e "${YELLOW}Cleaning up existing test repo at $REPO_PATH...${NC}"
    rm -rf "$REPO_PATH"
fi

# Create the test directory
mkdir -p "$REPO_PATH"
cd "$REPO_PATH"

# Initialize Git repo
echo -e "${CYAN}Initializing Git repository...${NC}"
git init
git config user.name "Test User"
git config user.email "test@example.com"

# Initial commit to set up repo
echo -e "# Test MonoRepo\nThis is a test monorepo for versioning." > README.md
git add README.md
git commit -m "Initial commit"

# Create the project structure
echo -e "${CYAN}Creating project structure...${NC}"

# Create directories
mkdir -p src/Core
mkdir -p src/Data
mkdir -p src/Api
mkdir -p src/UI
mkdir -p tests/Core.Tests
mkdir -p build

# Function to create a project
create_project() {
    local NAME=$1
    local PATH=$2
    local DEPS=$3
    local FILES=$4
    local IS_TEST=$5

    local PROJ_PATH="$REPO_PATH/$PATH"
    local CSPROJ_PATH="$PROJ_PATH/$NAME.csproj"
    
    # Create .csproj file
    echo "<Project Sdk=\"Microsoft.NET.Sdk\">" > "$CSPROJ_PATH"
    echo "  <PropertyGroup>" >> "$CSPROJ_PATH"
    echo "    <TargetFramework>net8.0</TargetFramework>" >> "$CSPROJ_PATH"
    echo "    <ImplicitUsings>enable</ImplicitUsings>" >> "$CSPROJ_PATH"
    echo "    <Nullable>enable</Nullable>" >> "$CSPROJ_PATH"
    echo "    <Version>$DEFAULT_VERSION</Version>" >> "$CSPROJ_PATH"
    
    if [ "$IS_TEST" == "true" ]; then
        echo "    <IsTestProject>true</IsTestProject>" >> "$CSPROJ_PATH"
    fi
    
    echo "  </PropertyGroup>" >> "$CSPROJ_PATH"
    echo "" >> "$CSPROJ_PATH"
    
    # Add dependencies
    if [ ! -z "$DEPS" ]; then
        echo "  <ItemGroup>" >> "$CSPROJ_PATH"
        
        IFS=',' read -ra DEP_ARRAY <<< "$DEPS"
        for DEP in "${DEP_ARRAY[@]}"; do
            local DEP_PATH=""
            
            case "$DEP" in
                "Core") DEP_PATH="src/Core" ;;
                "Data") DEP_PATH="src/Data" ;;
                "Api") DEP_PATH="src/Api" ;;
                "UI") DEP_PATH="src/UI" ;;
                "Tests") DEP_PATH="tests/Core.Tests" ;;
            esac
            
            local REL_PATH="..\/..\/..\/..\..\/$DEP_PATH\/$DEP.csproj"
            echo "    <ProjectReference Include=\"$REL_PATH\" />" >> "$CSPROJ_PATH"
        done
        
        echo "  </ItemGroup>" >> "$CSPROJ_PATH"
        echo "" >> "$CSPROJ_PATH"
    fi
    
    echo "  <!-- Import the Mister.Version targets -->" >> "$CSPROJ_PATH"
    echo "  <Import Project=\"\$(MSBuildThisFileDirectory)..\..\..\build\Mister.Version.targets\" />" >> "$CSPROJ_PATH"
    echo "</Project>" >> "$CSPROJ_PATH"
    
    # Create source files
    IFS=',' read -ra FILE_ARRAY <<< "$FILES"
    for FILE in "${FILE_ARRAY[@]}"; do
        local FILE_NAME="${FILE%.cs}"
        local FILE_PATH="$PROJ_PATH/$FILE"
        
        echo "namespace $NAME" > "$FILE_PATH"
        echo "{" >> "$FILE_PATH"
        echo "    public class $FILE_NAME" >> "$FILE_PATH"
        echo "    {" >> "$FILE_PATH"
        echo "        // Initial version" >> "$FILE_PATH"
        echo "        public string Version => \"v1\";" >> "$FILE_PATH"
        echo "    }" >> "$FILE_PATH"
        echo "}" >> "$FILE_PATH"
    done
}

# Create projects
create_project "Core" "src/Core" "" "CoreServices.cs,CoreModels.cs" "false"
create_project "Data" "src/Data" "Core" "DataRepository.cs,DataModels.cs" "false"
create_project "Api" "src/Api" "Core,Data" "ApiController.cs,Startup.cs" "false"
create_project "UI" "src/UI" "Core,Api" "UIComponents.cs,UIModels.cs" "false"
create_project "Tests" "tests/Core.Tests" "Core" "CoreTests.cs" "true"

# Create build directory with targets file
cat > "$REPO_PATH/build/Mister.Version.targets" << EOF
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <!-- Define properties -->
  <PropertyGroup>
    <MrVersionEnabled Condition="'\$(MrVersionEnabled)' == ''">true</MrVersionEnabled>
    <MrVersionRepoRoot Condition="'\$(MrVersionRepoRoot)' == ''">$(MSBuildThisFileDirectory)../</MrVersionRepoRoot>
    <MrVersionTagPrefix Condition="'\$(MrVersionTagPrefix)' == ''">v</MrVersionTagPrefix>
    <MrVersionUpdateProjectFile Condition="'\$(MrVersionUpdateProjectFile)' == ''">false</MrVersionUpdateProjectFile>
    <MrVersionDebug Condition="'\$(MrVersionDebug)' == ''">true</MrVersionDebug>
    <MrVersionSkipTestProjects Condition="'\$(MrVersionSkipTestProjects)' == ''">true</MrVersionSkipTestProjects>
    <MrVersionSkipNonPackableProjects Condition="'\$(MrVersionSkipNonPackableProjects)' == ''">true</MrVersionSkipNonPackableProjects>
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
    
    <Message Text="Mister.Version: Using version 1.0.0 for \$(MSBuildProjectName)" Importance="High" />
  </Target>
  
  <PropertyGroup>
    <BuildDependsOn>
      _MrVersionCalculate;
      \$(BuildDependsOn)
    </BuildDependsOn>
  </PropertyGroup>
</Project>
EOF

# Initial commit with all projects
git add --all
git commit -m "Add initial project structure"

# Tag initial version
git tag $TAG1
echo -e "${GREEN}Created initial tag: $TAG1${NC}"

# Run a test report of initial state
echo -e "${MAGENTA}Running initial version report...${NC}"
# Uncomment to run actual tool
# $TOOL_PATH report -r $REPO_PATH -o text
echo -e "${GRAY}[Simulated report] All projects at version 1.0.0${NC}"

# SCENARIO 1: Simple Core Change
echo -e "\n${YELLOW}SCENARIO 1: Simple Core Change${NC}"

# Modify Core project
sed -i 's/public string Version => "v1";/public string Version => "v2";/' "$REPO_PATH/src/Core/CoreModels.cs"
git add "$REPO_PATH/src/Core/CoreModels.cs"
git commit -m "Update Core models to v2"

# Simulate version checking with and without dependency tracking
echo -e "${MAGENTA}Checking versions after Core change...${NC}"
# $TOOL_PATH report -r $REPO_PATH -o text
echo -e "${GRAY}[Simulated report] Core should be at version 1.0.1, others remain at 1.0.0${NC}"

# Create v2.0.0 tag
git tag $TAG2
echo -e "${GREEN}Created new tag: $TAG2${NC}"

# SCENARIO 2: Changes with Dependencies
echo -e "\n${YELLOW}SCENARIO 2: Changes with Dependencies${NC}"

# Create a feature branch
git checkout -b feature/data-improvements

# Modify Data project
sed -i 's/public string Version => "v1";/public string Version => "v3";/' "$REPO_PATH/src/Data/DataModels.cs"
git add "$REPO_PATH/src/Data/DataModels.cs"
git commit -m "Update Data models to v3"

# Simulate version checking on a feature branch
echo -e "${MAGENTA}Checking versions on feature branch...${NC}"
# $TOOL_PATH report -r $REPO_PATH -o text
echo -e "${GRAY}[Simulated report] Data should have a feature branch version${NC}"

# Return to main branch and merge feature
git checkout master
git merge feature/data-improvements --no-ff -m "Merge data improvements"

# Simulate version checking after merge
echo -e "${MAGENTA}Checking versions after feature merge...${NC}"
# $TOOL_PATH report -r $REPO_PATH -o text
echo -e "${GRAY}[Simulated report] Data and dependent projects should have new versions${NC}"

# SCENARIO 3: Release Branch
echo -e "\n${YELLOW}SCENARIO 3: Release Branch${NC}"

# Create a release branch from v2 tag
git checkout -b release/v2.0 $TAG2

# Fix a bug in Core
sed -i 's/public string Version => "v1";/public string Version => "v2.1-hotfix";/' "$REPO_PATH/src/Core/CoreServices.cs"
git add "$REPO_PATH/src/Core/CoreServices.cs"
git commit -m "Hotfix for Core services"

# Simulate version checking on release branch
echo -e "${MAGENTA}Checking versions on release branch...${NC}"
# $TOOL_PATH report -r $REPO_PATH -b release/v2.0 -o text
echo -e "${GRAY}[Simulated report] Core should be at version 2.0.1 on release branch${NC}"

# SCENARIO 4: Complex Dependency Chain
echo -e "\n${YELLOW}SCENARIO 4: Complex Dependency Chain${NC}"

# Return to main branch
git checkout master

# Create v3.0.0 tag
git tag $TAG3
echo -e "${GREEN}Created new tag: $TAG3${NC}"

# Modify UI project
sed -i 's/public string Version => "v1";/public string Version => "v4";/' "$REPO_PATH/src/UI/UIModels.cs"
git add "$REPO_PATH/src/UI/UIModels.cs"
git commit -m "Update UI models to v4"

# Now modify Core (which UI depends on indirectly)
if [ -f "$REPO_PATH/src/Core/CoreServices.cs" ]; then
    sed -i 's/public string Version => "v1";/public string Version => "v4";/' "$REPO_PATH/src/Core/CoreServices.cs"
    git add "$REPO_PATH/src/Core/CoreServices.cs"
    git commit -m "Update Core services to v4"
fi

# Simulate version checking to test dependency chain
echo -e "${MAGENTA}Checking versions after complex dependency changes...${NC}"
# $TOOL_PATH report -r $REPO_PATH -o text
echo -e "${GRAY}[Simulated report] Core at 3.0.1, UI at 3.0.1, Api and Data should also change due to dependencies${NC}"

# SCENARIO 5: Test Project Changes
echo -e "\n${YELLOW}SCENARIO 5: Test Project Changes${NC}"

# Modify test project
sed -i 's/public string Version => "v1";/public string Version => "v5-tests";/' "$REPO_PATH/tests/Core.Tests/CoreTests.cs"
git add "$REPO_PATH/tests/Core.Tests/CoreTests.cs"
git commit -m "Update tests to v5"

# Simulate version checking to verify test projects are excluded
echo -e "${MAGENTA}Checking versions after test changes...${NC}"
# $TOOL_PATH report -r $REPO_PATH -o text
echo -e "${GRAY}[Simulated report] Test project changes should not affect versions${NC}"

# Final summary
echo -e "\n${GREEN}Test Repository Created Successfully:${NC}"
echo -e "${CYAN}Location: $REPO_PATH${NC}"
echo -e "${CYAN}Projects: 5${NC}"
echo -e "${CYAN}Tags: $TAG1, $TAG2, $TAG3${NC}"
echo -e "${CYAN}Branches: master, feature/data-improvements, release/v2.0${NC}"

# Run the version reporter in different formats
echo -e "\n${YELLOW}To test the monorepo versioning tool on this repo, run:${NC}"
echo -e "  $TOOL_PATH report -r $REPO_PATH"
echo -e "  $TOOL_PATH report -r $REPO_PATH -o json -f report.json"
echo -e "  $TOOL_PATH report -r $REPO_PATH -b release/v2.0"
echo -e "  $TOOL_PATH version -r $REPO_PATH -p $REPO_PATH/src/Core/Core.csproj -d"

echo -e "\n${GREEN}Test completed successfully!${NC}"