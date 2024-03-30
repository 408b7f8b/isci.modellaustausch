#!/bin/bash

mkdir tmp

# Get the current directory
current_directory=$(dirname "${BASH_SOURCE[0]}")

# Find the .csproj file in the current directory
csproj_file=$(find "$current_directory" -maxdepth 1 -type f -name "*.csproj" -print -quit)

# Extract the filename from the path
csproj_filename=$(basename "$csproj_file" .csproj)

# Print the name of the .csproj file
echo "The name of the .csproj file is: $csproj_filename"

# Ask the user for the version tag
read -p "Enter the version tag: " version_tag

# Build and publish the dotnet project for different architectures
dotnet publish -c Release -r linux-x64 --self-contained -p:GenerateRuntimeConfigurationFiles=false -p:GenerateDependencyFile=false
rm -rf tmp/*
cp bin/Release/netcoreapp8.0/linux-x64/publish/* tmp/
docker build -t localhost:2000/${csproj_filename}.linux-x64:${version_tag} . --platform="linux/amd64"
docker push localhost:2000/${csproj_filename}.linux-x64:${version_tag}

dotnet publish -c Release -r linux-arm --self-contained -p:GenerateRuntimeConfigurationFiles=false -p:GenerateDependencyFile=false 
rm -rf tmp/*
cp bin/Release/netcoreapp8.0/linux-arm/publish/* tmp/
docker build -t localhost:2000/${csproj_filename}.linux-arm:${version_tag} . --platform="linux/arm/v7"
docker push localhost:2000/${csproj_filename}.linux-arm:${version_tag}

#dotnet publish -c Release -r win-x64
#rm -rf tmp/*
#cp bin/Release/netcoreapp8.0/win-x64/publish/* tmp/
#docker build -t localhost:2000/${csproj_filename}.win-x64:${version_tag} .
#docker push localhost:2000/${csproj_filename}.win-x64:${version_tag}
