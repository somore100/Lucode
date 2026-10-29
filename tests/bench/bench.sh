#!/usr/bin/env bash
# Times tests/bench/bench.luc. Not part of the test suite: it only tells you how fast Lucode is.
cd "$(dirname "$0")/../.."
dotnet build src/Lucode -c Release -o build --nologo -v quiet >/dev/null || exit 1
time dotnet build/lucode.dll tests/bench/bench.luc
