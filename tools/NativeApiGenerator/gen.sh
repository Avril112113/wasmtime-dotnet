#!/usr/bin/env bash
# Exit script if any commands fail.
set -e

WASMTIME_VERSION=v44.0.2

wget https://github.com/bytecodealliance/wasmtime/releases/download/$WASMTIME_VERSION/wasmtime-$WASMTIME_VERSION-x86_64-linux-c-api.tar.xz
tar xf *.tar.xz

cargo run --manifest-path ./wasmtime-csbindgen/Cargo.toml ./wasmtime-$WASMTIME_VERSION-x86_64-linux-c-api

git clone https://github.com/bytecodealliance/wasmtime.git --branch $WASMTIME_VERSION --depth 1

sed -i -e '/GENERATE_XML/ s/NO/YES/' wasmtime/crates/c-api/doxygen.conf.in
sed -i -e '/WARN_AS_ERROR/ s/YES/NO/' wasmtime/crates/c-api/doxygen.conf.in

cmake -S wasmtime/crates/c-api -B wasmtime/target/c-api
cmake --build wasmtime/target/c-api --target doc

dotnet run --project WasmtimeCsbindgenRewriter/WasmtimeCsbindgenRewriter/WasmtimeCsbindgenRewriter.csproj NativeWasmtime.g.cs wasmtime/crates/c-api/xml
