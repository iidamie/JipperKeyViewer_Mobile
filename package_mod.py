import shutil
import sys
import zipfile
from pathlib import Path


MOD_ID = "JipperKeyViewer"
ROOT = Path(__file__).resolve().parent


def find_dll() -> Path | None:
    candidates = [
        ROOT / "MobilePlugin" / "bin" / "Release" / "net10.0" / f"{MOD_ID}.dll",
        ROOT / f"{MOD_ID}.dll",
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    return None


def main() -> int:
    version = (ROOT / "VERSION.txt").read_text(encoding="utf-8").strip()
    dll = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else find_dll()
    if not version or dll is None or not dll.is_file():
        print("Build MobilePlugin/JipperKeyViewer.csproj first, or pass the DLL path.", file=sys.stderr)
        return 1

    output = ROOT / f"{MOD_ID}-{version}.zip"
    staging = ROOT / "tmp_package"
    mod_dir = staging / MOD_ID
    if staging.exists():
        shutil.rmtree(staging)
    if output.exists():
        output.unlink()
    mod_dir.mkdir(parents=True)

    shutil.copy2(dll, mod_dir / f"{MOD_ID}.dll")
    license_path = ROOT.parent / "LICENSE.txt"
    if license_path.is_file():
        shutil.copy2(license_path, mod_dir / "LICENSE.txt")

    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(mod_dir.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(staging))

    shutil.rmtree(staging)
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
