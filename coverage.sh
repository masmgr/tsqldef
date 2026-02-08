#!/bin/bash
# テストカバレッジレポートを生成するスクリプト (Linux/macOS用)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
RESULTS_DIR="$REPO_ROOT/TestResults"
REPORT_DIR="$RESULTS_DIR/CoverageReport"
SOLUTION="$REPO_ROOT/SqlSchemaDef.sln"
RUNSETTINGS="$REPO_ROOT/coverlet.runsettings"

OPEN_REPORT=false
THRESHOLD=0

while [[ $# -gt 0 ]]; do
    case $1 in
        --open) OPEN_REPORT=true; shift ;;
        --threshold) THRESHOLD=$2; shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

# Clean previous results
rm -rf "$RESULTS_DIR"

echo "Running tests with coverage collection..."

dotnet test "$SOLUTION" \
    --collect "XPlat Code Coverage" \
    --settings "$RUNSETTINGS" \
    --results-directory "$RESULTS_DIR"

# Find coverage file
COVERAGE_FILE=$(find "$RESULTS_DIR" -name "coverage.cobertura.xml" -type f | head -1)

if [ -z "$COVERAGE_FILE" ]; then
    echo "No coverage file found."
    exit 1
fi

echo "Coverage file: $COVERAGE_FILE"

# Install ReportGenerator if not already installed
if ! dotnet tool list --global 2>&1 | grep -q "dotnet-reportgenerator-globaltool"; then
    echo "Installing ReportGenerator..."
    dotnet tool install --global dotnet-reportgenerator-globaltool
fi

# Generate report
echo "Generating HTML coverage report..."

reportgenerator \
    "-reports:$COVERAGE_FILE" \
    "-targetdir:$REPORT_DIR" \
    "-reporttypes:Html;TextSummary;MarkdownSummaryGithub"

# Display summary
SUMMARY_FILE="$REPORT_DIR/Summary.txt"
if [ -f "$SUMMARY_FILE" ]; then
    echo ""
    echo "=== Coverage Summary ==="
    cat "$SUMMARY_FILE"
    echo ""
fi

# Check threshold
if [ "$THRESHOLD" -gt 0 ]; then
    LINE_COVERAGE=$(grep -oP 'Line coverage:\s*\K[\d.]+' "$SUMMARY_FILE" || echo "0")
    if [ "$(echo "$LINE_COVERAGE < $THRESHOLD" | bc)" -eq 1 ]; then
        echo "Line coverage (${LINE_COVERAGE}%) is below threshold (${THRESHOLD}%)."
        exit 1
    fi
    echo "Line coverage (${LINE_COVERAGE}%) meets threshold (${THRESHOLD}%)."
fi

echo "Report generated at: $REPORT_DIR"

# Open in browser
if [ "$OPEN_REPORT" = true ]; then
    INDEX_FILE="$REPORT_DIR/index.html"
    if [ -f "$INDEX_FILE" ]; then
        if command -v xdg-open &> /dev/null; then
            xdg-open "$INDEX_FILE"
        elif command -v open &> /dev/null; then
            open "$INDEX_FILE"
        fi
    fi
fi
