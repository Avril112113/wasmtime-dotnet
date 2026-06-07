use std::fs;

fn main() {
    let args = std::env::args().collect::<Vec<_>>();
    // First arg is always the executable.
    if args.len() != 2 {
        panic!("Expected 1 argument for path to wasmtime c-api dir, but got {}", args.len());
    }

    let wasmtime_c_api_dir = std::path::Path::new(&args[1]);
    let include_dir = wasmtime_c_api_dir.join("include");
    let temp_rs_path = wasmtime_c_api_dir.join("..").join("temp_wasmtime_c_api.rs");
    let out_path = std::path::Path::new("./NativeWasmtime.g.cs");

    bindgen::Builder::default()
        .header(include_dir.join("wasmtime.h").to_string_lossy())
        // wasmtime uses `<xxx.h>`, which clang expect for system headers, so we just add the dir as system include directory.
        .clang_arg("-isystem").clang_arg(include_dir.to_string_lossy())
        .default_enum_style(bindgen::EnumVariation::Rust {
            non_exhaustive: false,
        })
        .generate().unwrap()
        .write_to_file(&temp_rs_path).unwrap();

    let mut builder = csbindgen::Builder::default();
    builder = builder.input_bindgen_file(&temp_rs_path);
    builder
        .csharp_dll_name("wasmtime")
        .csharp_namespace("Wasmtime.Native")
        .csharp_generate_const_filter(|name| name.starts_with("WASM"))
        .method_filter(|name| name.starts_with("wasm"))
        .generate_csharp_file(&out_path).unwrap();

    fs::remove_file(temp_rs_path).unwrap_or_else(|err| println!("Failed to cleanup temp file: {}", err));
}
