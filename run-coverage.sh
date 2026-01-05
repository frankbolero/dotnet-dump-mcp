#!/bin/bash
set -e

echo "Running tests with code coverage..."
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults

echo "Generating HTML coverage report..."
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"TestResults/CoverageReport" -reporttypes:Html


echo ""
echo "Coverage report generated successfully!"
echo "Open the report: TestResults/CoverageReport/index.html"
