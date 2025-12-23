#!/bin/bash
# Build script for GitHubArchiver.Daemon
# Produces self-contained single-file executables for Windows and Linux

set -e

CONFIGURATION="${1:-Release}"
OUTPUT_DIR="publish/GitHubArchiver.Daemon"
PROJECT_PATH="GitHubArchiver.Daemon/GitHubArchiver.Daemon.csproj"

# Clean output directory
rm -rf "$OUTPUT_DIR"

echo "Building GitHubArchiver.Daemon..."

COMMON_ARGS=(
    "--configuration" "$CONFIGURATION"
    "-p:PublishSingleFile=true"
    "-p:SelfContained=true"
    "-p:IncludeNativeLibrariesForSelfExtract=true"
    "-p:EnableCompressionInSingleFile=true"
    "-p:DebugType=none"
    "-p:DebugSymbols=false"
)

# Build for Linux x64
echo ""
echo "Publishing for Linux x64..."
dotnet publish "$PROJECT_PATH" \
    --runtime linux-x64 \
    --output "$OUTPUT_DIR/linux-x64" \
    "${COMMON_ARGS[@]}"

# Build for Windows x64
echo ""
echo "Publishing for Windows x64..."
dotnet publish "$PROJECT_PATH" \
    --runtime win-x64 \
    --output "$OUTPUT_DIR/win-x64" \
    "${COMMON_ARGS[@]}"

echo ""
echo "Build complete!"
echo "Output locations:"
echo "  Linux:   $OUTPUT_DIR/linux-x64/"
echo "  Windows: $OUTPUT_DIR/win-x64/"

echo ""
echo "Output files:"
find "$OUTPUT_DIR" -type f -exec ls -lh {} \;
