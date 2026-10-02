//! Mu3D's versioned DXC bytecode cache. No renderer-specific resources live here.
//! Copied into the pinned wgpu-hal source by the maintainer build script.
use sha2::{Digest, Sha256};
use alloc::{format, string::String, vec, vec::Vec};
use std::{collections::HashMap, ffi::OsString, fs, io::Write, path::{Path, PathBuf}, sync::{Mutex, OnceLock, atomic::{AtomicU64, Ordering}}, time::Instant};

const INPUT_MAGIC: &[u8] = b"Mu3D.dxil.v1.wgpu29.0.3\0";
const BLOB_MAGIC: &[u8] = b"M3DXIL01";
const MAX_BLOB: usize = 16 * 1024 * 1024;
static SEQUENCE: AtomicU64 = AtomicU64::new(0);

#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetModuleHandleExW(flags: u32, address: *const u16, module: *mut *mut core::ffi::c_void) -> i32;
    fn GetModuleFileNameW(module: *mut core::ffi::c_void, name: *mut u16, size: u32) -> u32;
}

fn compiler_identity(compiler: &windows::Win32::Graphics::Direct3D::Dxc::IDxcCompiler3) -> Option<[u8; 32]> {
    // Resolve the actual COM implementation, not a possibly different same-named DLL loaded
    // by another graphics library in the host. The compiler container retains its module.
    let address = windows::core::Interface::vtable(compiler).Compile as *const ();
    let mut module = core::ptr::null_mut();
    if unsafe { GetModuleHandleExW(6, address.cast(), &mut module) } == 0 { return None; }
    static IDENTITIES: OnceLock<Mutex<HashMap<usize, Option<[u8; 32]>>>> = OnceLock::new();
    let mut identities = IDENTITIES.get_or_init(|| Mutex::new(HashMap::new())).lock().ok()?;
    if let Some(value) = identities.get(&(module as usize)) { return *value; }
    let value = (|| {
        use std::os::windows::ffi::OsStringExt;
        let mut path = vec![0u16; 32768];
        let count = unsafe { GetModuleFileNameW(module, path.as_mut_ptr(), path.len() as u32) } as usize;
        if count == 0 || count >= path.len() { return None; }
        let bytes = fs::read(PathBuf::from(OsString::from_wide(&path[..count]))).ok()?;
        Some(Sha256::digest(bytes).into())
    })();
    identities.insert(module as usize, value);
    value
}

fn configured_path(name: &str) -> Option<PathBuf> {
    std::env::var_os(name).map(PathBuf::from).filter(|p| p.is_absolute())
}

pub(super) struct Entry {
    key: String,
    input: Vec<u8>,
    writable: Option<PathBuf>,
    started: Instant,
}

impl Entry {
    pub(super) fn new(source: &str, args: &[windows::core::PCWSTR], compiler: &windows::Win32::Graphics::Direct3D::Dxc::IDxcCompiler3) -> Option<Self> {
        if std::env::var_os("MU3D_SHADER_CACHE_DISABLE").as_deref() == Some(std::ffi::OsStr::new("1")) { return None; }
        let mut input = INPUT_MAGIC.to_vec();
        input.extend_from_slice(&compiler_identity(compiler)?);
        input.extend_from_slice(&(args.len() as u32).to_le_bytes());
        for arg in args {
            let text = unsafe { arg.to_string().ok()? };
            input.extend_from_slice(&(text.len() as u32).to_le_bytes());
            input.extend_from_slice(text.as_bytes());
        }
        input.extend_from_slice(&(source.len() as u64).to_le_bytes());
        input.extend_from_slice(source.as_bytes());
        let key = format!("{:x}", Sha256::digest(&input));
        let writable = configured_path("MU3D_SHADER_CACHE_DIR").or_else(|| {
            configured_path("LOCALAPPDATA").map(|p| p.join("Mu3D/ShaderCache/wgpu29-v1"))
        });
        Some(Self { key, input, writable, started: Instant::now() })
    }

    pub(super) fn load(&self) -> Option<Vec<u8>> {
        // Packaged, read-only seeds have priority over disposable per-user cache entries.
        let seed = configured_path("MU3D_SHADER_CACHE_SEEDS").or_else(|| {
            std::env::current_exe().ok()?.parent().map(|p| p.join("Mu3D/ShaderCache"))
        });
        for (kind, root) in [("bundle", seed.as_ref()), ("disk", self.writable.as_ref())] {
            if let Some(root) = root {
                if let Some(blob) = read_blob(&root.join(format!("{}.bin", self.key))) {
                    log::info!("Mu3D DXIL cache {kind} hit {} ({:.2} ms)", self.key, self.started.elapsed().as_secs_f64() * 1000.0);
                    return Some(blob);
                }
            }
        }
        None
    }

    pub(super) fn store(&self, blob: &[u8]) {
        log::info!("Mu3D DXIL compile {} ({:.2} ms)", self.key, self.started.elapsed().as_secs_f64() * 1000.0);
        if blob.len() > MAX_BLOB || !blob.starts_with(b"DXBC") { return; }
        let mut encoded = BLOB_MAGIC.to_vec();
        encoded.extend_from_slice(&Sha256::digest(blob));
        encoded.extend_from_slice(blob);
        if let Some(root) = &self.writable {
            // Bound opportunistic storage to 256 MiB / 1024 entries. A full cache is a miss,
            // never a rendering failure. No application or bundled files are deleted.
            let mut count = 0;
            let mut bytes = 0u64;
            if let Ok(files) = fs::read_dir(root) {
                for file in files.flatten().take(1025) {
                    count += 1;
                    bytes = bytes.saturating_add(file.metadata().map(|m| m.len()).unwrap_or(0));
                }
            }
            if count < 1024 && bytes + encoded.len() as u64 <= 256 * 1024 * 1024 {
                atomic_write(root, &format!("{}.bin", self.key), &encoded);
            }
        }
        if let Some(root) = configured_path("MU3D_SHADER_CACHE_CAPTURE") {
            atomic_write(&root, &format!("{}.input", self.key), &self.input);
            atomic_write(&root, &format!("{}.bin", self.key), &encoded);
        }
    }
}

fn read_blob(path: &Path) -> Option<Vec<u8>> {
    use std::io::Read;
    let file = fs::File::open(path).ok()?;
    let length = file.metadata().ok()?.len();
    if !(44..=(MAX_BLOB + 40) as u64).contains(&length) { return None; }
    let mut bytes = Vec::with_capacity(length as usize);
    file.take((MAX_BLOB + 41) as u64).read_to_end(&mut bytes).ok()?;
    if bytes.len() != length as usize || &bytes[..8] != BLOB_MAGIC || &bytes[40..44] != b"DXBC" { return None; }
    if Sha256::digest(&bytes[40..]).as_slice() != &bytes[8..40] { return None; }
    Some(bytes.split_off(40))
}

fn atomic_write(root: &Path, name: &str, bytes: &[u8]) {
    let write = || -> std::io::Result<()> {
        fs::create_dir_all(root)?;
        let temporary = root.join(format!(".{}-{}.tmp", std::process::id(), SEQUENCE.fetch_add(1, Ordering::Relaxed)));
        let mut file = fs::OpenOptions::new().write(true).create_new(true).open(&temporary)?;
        let result = file.write_all(bytes).and_then(|_| file.sync_all());
        drop(file);
        let result = result.and_then(|_| fs::rename(&temporary, root.join(name)));
        if result.is_err() { let _ = fs::remove_file(&temporary); }
        result
    };
    let _ = write(); // Cache permissions, contention and disk space never prevent compilation.
}
