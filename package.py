import argparse
import re
import subprocess
import tempfile
import zipfile
from pathlib import Path


PROJECT = "boksol"
ROOT_FILES = (".gitignore", "README.md", "build.ps1", "package.py", "说明.txt")
SERVER_FILES = (".gitignore", "Boksol.Server.csproj", "Program.cs", "README.md")
VENDOR_TREES = ("vbam", "melonds", "native-deps/enet")
VENDOR_EXCLUDED_DIRS = {".git", ".vs", ".idea", "build", "CMakeFiles", "__pycache__"}
VENDOR_EXCLUDED_SUFFIXES = {
    ".gba", ".nds", ".srl", ".rom", ".sav", ".sa1", ".sgm", ".ml1",
    ".exe", ".dll", ".so", ".o", ".obj", ".a", ".lib", ".pdb",
    ".zip", ".bin", ".pcap", ".pkt", ".pyc",
}
LOCAL_ASSETS = (
    "src/solar.ico", "src/solar.png",
    "src/assets/boktai2.sav", "src/assets/boktai2.start.sgm",
    "src/assets/boktai3.sav", "src/assets/boktai3.start.sgm",
    "src/assets/boktaids.sav", "src/assets/boktaids.host.start.ml1",
    "src/assets/boktaids.guest.start.ml1",
)


def vendor_files(folder: Path):
    if (folder / ".git").exists():
        tracked = subprocess.check_output(["git", "-C", str(folder), "ls-files", "-z"]).decode("utf-8")
        files = (folder / name for name in tracked.split("\0") if name)
    else:
        files = folder.rglob("*")

    for path in files:
        parts = path.relative_to(folder).parts
        if (VENDOR_EXCLUDED_DIRS.intersection(parts) or path.name == "CMakeCache.txt" or
                path.suffix.lower() in VENDOR_EXCLUDED_SUFFIXES):
            continue
        if path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(folder.resolve()):
            yield path


def source_files(work: Path, include_local_assets: bool):
    files = [work / name for name in ROOT_FILES]
    files.extend(work / "server" / name for name in SERVER_FILES)
    files.extend((work / "src").glob("*.cs"))
    android = work / "android"
    if android.is_dir():
        files.extend(android.glob("*.cs"))
        files.extend(android / name for name in ("Boksol.Android.csproj", "build.ps1", "Properties/AndroidManifest.xml"))
    files.extend(path for path in (work / "native").rglob("*")
                 if path.is_file() and (path.suffix in {".cpp", ".h", ".cmake", ".md"} or path.name == "CMakeLists.txt"))
    for vendor in VENDOR_TREES:
        if not (work / vendor / "CMakeLists.txt").is_file():
            raise FileNotFoundError(f"Missing emulator source tree: {vendor}")
        files.extend(vendor_files(work / vendor))
    if include_local_assets:
        files.extend(work / name for name in LOCAL_ASSETS if (work / name).is_file())
    return {path.relative_to(work).as_posix(): path for path in sorted(files)}


def write_archive(target: Path, contents: dict[str, Path]) -> Path:
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=target.parent, suffix=".tmp", delete=False) as temp:
        temporary = Path(temp.name)
    try:
        with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
            for name, path in contents.items():
                bundle.write(path, name)
        with zipfile.ZipFile(temporary) as bundle:
            if set(bundle.namelist()) != set(contents) or bundle.testzip() is not None:
                raise ValueError(f"Could not verify {target.name}")
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)
    return target


def package_source(work: Path, version: str) -> Path:
    target = work / "outputs" / f"{PROJECT}-{version}-github-source.zip"
    return write_archive(target, source_files(work, include_local_assets=False))


def package_server(work: Path, version: str) -> Path:
    target = work / "outputs" / f"{PROJECT}-{version}-server-source.zip"
    return write_archive(target, {name: work / "server" / name for name in SERVER_FILES})


def package(work: Path, version: str) -> Path:
    stage = work / "stage"
    executable = f"{PROJECT}.exe"
    contents = {
        executable: stage / executable,
        "使用说明.txt": work / "说明.txt",
        "engines/gds-gba.dll": stage / "engines/gds-gba.dll",
        "engines/gds-ds.dll": stage / "engines/gds-ds.dll",
        "engines/msvcp140.dll": stage / "engines/msvcp140.dll",
        "engines/vcruntime140.dll": stage / "engines/vcruntime140.dll",
        "engines/vcruntime140_1.dll": stage / "engines/vcruntime140_1.dll",
        "engines/VBA-M-LICENSE.txt": work / "vbam/doc/License.txt",
        "engines/GPL-2.0.txt": work / "vbam/doc/gpl.txt",
        "engines/melonDS-LICENSE.txt": work / "melonds/LICENSE",
        "engines/ENet-LICENSE.txt": work / "native-deps/enet/LICENSE",
    }
    missing = [str(path.relative_to(work)) for path in contents.values() if not path.is_file()]
    if missing:
        raise FileNotFoundError("Build and stage these files first: " + ", ".join(missing))

    source = stage / f"{PROJECT}-source.zip"
    contents[source.name] = write_archive(source, source_files(work, include_local_assets=True))
    target = work / "outputs" / f"{PROJECT}-{version}-Windows-x64.zip"
    return write_archive(target, {f"{PROJECT}/{name}": path for name, path in contents.items()})


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Package the Windows application or GitHub source tree.")
    parser.add_argument("version", help="version used in the ZIP filename, e.g. 0.5.4")
    scope = parser.add_mutually_exclusive_group()
    scope.add_argument("--source", action="store_true", help="export the clean GitHub source tree")
    scope.add_argument("--server", action="store_true", help="export only the standalone signaling server source")
    arguments = parser.parse_args()
    if not re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+-]*", arguments.version):
        parser.error("version must contain only ASCII letters, numbers, periods, plus signs or hyphens")
    create_archive = package_server if arguments.server else package_source if arguments.source else package
    print(create_archive(Path(__file__).resolve().parent, arguments.version))
