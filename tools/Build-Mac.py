"""Publish two independent, self-contained macOS TV companions, preserving Unix executable modes."""
import argparse
import hashlib
import json
import platform
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import urllib.request
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile, ZipInfo

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "Companion" / "LivingPartner.Mac"
MODEL = "ggml-small-q5_1.bin"
SHA = "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb"
MODEL_URL = f"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{MODEL}?download=true"


def check(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def obtain_model(path):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and check(path) == SHA:
        return
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as out:
        downloaded = Path(out.name)
        try:
            with urllib.request.urlopen(MODEL_URL, timeout=120) as src:
                shutil.copyfileobj(src, out)
        except BaseException:
            out.close()
            downloaded.unlink(missing_ok=True)
            raise
    if check(downloaded) != SHA:
        downloaded.unlink()
        raise ValueError("Unexpected Whisper model SHA256")
    downloaded.replace(path)


def include_asset(relative, arch):
    # Whisper.net copies native libraries for every CPU to a generic build.
    if relative.parts[0] == "runtimes":
        return relative.parts[1] in {f"macos-{arch}", f"osx-{arch}", "osx"}
    return relative.suffix not in {".pdb"} and relative.name not in {"web.config"}


def add_file(archive, path, name, executable=False):
    item = ZipInfo("Bambi TV/" + name)
    item.create_system = 3
    item.external_attr = (0o100755 if executable else 0o100644) << 16
    item.compress_type = ZIP_STORED if path.name == MODEL else ZIP_DEFLATED
    if path.suffix == ".command":
        archive.writestr(item, path.read_bytes().replace(b"\r\n", b"\n"))
        return
    with path.open("rb") as source, archive.open(item, "w") as dest:
        shutil.copyfileobj(source, dest)


def signer():
    if platform.system() == "Darwin":
        return ["/usr/bin/codesign", "--force", "--sign", "-"]
    if tool := shutil.which("rcodesign"):
        return [tool, "sign"]
    if platform.system() != "Windows":
        raise SystemExit("Install rcodesign for cross-signing, or run the build on macOS.")
    # Packages built from Windows need an ad-hoc signature too, including for Apple Silicon.
    cache = ROOT / "artifacts/tmp/mac-display"
    cache.mkdir(parents=True, exist_ok=True)
    tool = cache / "rcodesign.exe"
    digest = "4763d9e8a66a01a5cff91ad77071ec8a7032f2ebd103ba78fccddab397bb5dea"
    if not tool.exists() or check(tool) != digest:
        release = "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/0.29.0/apple-codesign-0.29.0-x86_64-pc-windows-msvc.zip"
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "tool.zip"
            urllib.request.urlretrieve(release, path)
            if check(path) != "54bb500e2da7a8de02fcae0f331d1cac6e6d7173b4281042ff9c528ba3159aaa":
                raise ValueError("Unexpected rcodesign release digest")
            with ZipFile(path) as archive:
                binary = archive.read(next(n for n in archive.namelist() if n.endswith("rcodesign.exe")))
                if hashlib.sha256(binary).hexdigest() != digest:
                    raise ValueError("Unexpected rcodesign executable digest")
                tool.write_bytes(binary)
    return [str(tool), "sign"]


def signature_offset(binary):
    # Thin little-endian Mach-O. Both apphosts and the per-architecture Whisper libraries use this layout.
    count = struct.unpack_from("<I", binary, 16)[0]
    at = 32
    for _ in range(count):
        command, size = struct.unpack_from("<II", binary, at)
        if command == 0x1D:
            return struct.unpack_from("<I", binary, at + 8)[0]
        at += size
    return 0


def verify_code_pages(binary):
    at = signature_offset(binary)
    if not at or struct.unpack_from(">I", binary, at)[0] != 0xFADE0CC0:
        raise ValueError("Missing embedded Mach-O signature")
    count = struct.unpack_from(">I", binary, at + 8)[0]
    for i in range(count):
        kind, offset = struct.unpack_from(">II", binary, at + 12 + 8*i)
        if kind != 0:
            continue
        cd = at + offset
        hashes, _, _, slots, limit = struct.unpack_from(">5I", binary, cd + 16)
        hsize, alg, _, pg = struct.unpack_from(">4B", binary, cd + 36)
        digest = {1:hashlib.sha1, 2:hashlib.sha256, 3:hashlib.sha256, 4:hashlib.sha384}[alg]
        page = 1 << pg
        if slots != (limit + page - 1) // page or slots == 0:
            raise ValueError("Invalid code signature coverage")
        for n in range(slots):
            start = cd + hashes + n*hsize
            if digest(binary[n*page:min(limit, (n+1)*page)]).digest()[:hsize] != binary[start:start+hsize]:
                raise ValueError("Mach-O signature page verification failed")
        return
    raise ValueError("Missing primary CodeDirectory")


def package_notices(archive, rid):
    assets = json.loads((PROJECT / "obj/project.assets.json").read_text(encoding="utf-8-sig"))
    roots = [Path(p) for p in assets["packageFolders"]]
    for package, metadata in assets["libraries"].items():
        if metadata.get("type") != "package":
            continue
        folder = next((r / metadata["path"] for r in roots if (r / metadata["path"]).is_dir()), None)
        if not folder:
            continue
        lowered = package.lower()
        if ".runtime.osx-" in lowered and rid not in lowered:
            continue
        for notice in folder.iterdir():
            if notice.is_file() and notice.name.lower().startswith(("license", "third-party-notice", "thirdpartynotice")):
                add_file(archive, notice, "Licenses/" + package.replace("/", "-") + "-" + notice.name)


def publish(arch, model, out, quest_apk):
    rid = "osx-" + arch
    with tempfile.TemporaryDirectory(prefix="bambi-mac-publish-") as temporary:
        build = Path(temporary) / "app"
        subprocess.run(["dotnet", "publish", str(PROJECT), "-c", "Release", "-r", rid,
                        "--self-contained", "true", "-o", str(build), "--nologo"], cwd=ROOT, check=True)
        native = build / "LivingPartner.Mac"
        header = native.read_bytes()[:8]
        if header[:4] != bytes.fromhex("cffaedfe") or int.from_bytes(header[4:8], "little") != (0x0100000c if arch == "arm64" else 0x01000007):
            raise ValueError(f"{rid} apphost is not the expected native Mach-O executable")
        if not (build / "runtimes" / ("macos-" + arch) / "libwhisper.dylib").exists():
            raise FileNotFoundError(f"{rid} native speech runtime is missing")
        sign = signer()
        for path in [native, *(build / "runtimes" / ("macos-" + arch)).glob("*.dylib")]:
            binary = path.read_bytes()
            if not signature_offset(binary):
                subprocess.run([*sign, str(path)], check=True)
                binary = path.read_bytes()
            verify_code_pages(binary)
        dest = out / f"LivingPartner-Mac-{arch}.zip"
        with ZipFile(dest.with_suffix(".building"), "w") as archive:
            add_file(archive, PROJECT / "Start-Bambi.command", "Start-Bambi.command", True)
            add_file(archive, PROJECT / "Install-Quest.command", "Install-Quest.command", True)
            if quest_apk:
                add_file(archive, quest_apk, "Quest/LivingAIPartner.apk")
            add_file(archive, PROJECT / "README.md", "README.md")
            for license_file in sorted(PROJECT.glob("*-LICENSE.txt")):
                add_file(archive, license_file, "Licenses/" + license_file.name)
            package_notices(archive, rid)
            for path in sorted(build.rglob("*")):
                rel = path.relative_to(build)
                if path.is_file() and include_asset(rel, arch):
                    add_file(archive, path, "runtime/" + rel.as_posix(),
                             path == native or path.suffix == ".dylib" or path.name == "createdump")
            add_file(archive, model, "runtime/Models/" + MODEL)
        dest.with_suffix(".building").replace(dest)
        print(f"{check(dest)}  {dest}")


if __name__ == "__main__":
    arg = argparse.ArgumentParser(description=__doc__)
    arg.add_argument("--arch", choices=["arm64","x64","both"], default="both")
    arg.add_argument("--model", type=Path, default=ROOT / "artifacts/tmp/mac-display" / MODEL)
    arg.add_argument("--out", type=Path, default=ROOT / "dist/LivingPartner-Mac")
    arg.add_argument("--quest-apk", type=Path, help="Quest APK to bundle. Defaults to dist/LivingAIPartner-Quest/LivingAIPartner.apk if present.")
    opts = arg.parse_args()
    quest_apk = opts.quest_apk
    if quest_apk is None:
        standard_apk = ROOT / "dist/LivingAIPartner-Quest/LivingAIPartner.apk"
        quest_apk = standard_apk if standard_apk.is_file() else None
    if quest_apk:
        if not quest_apk.is_file():
            arg.error(f"Missing Quest APK: {quest_apk}")
        with ZipFile(quest_apk) as apk:
            if not {"AndroidManifest.xml", "classes.dex"}.issubset(apk.namelist()):
                arg.error(f"Invalid Quest APK: {quest_apk}")
        print(f"Bundling Quest APK SHA256 {check(quest_apk)}")
    else:
        print("No Quest build present. Publishing Mac companion only.")
    obtain_model(opts.model)
    opts.out.mkdir(parents=True, exist_ok=True)
    for arch in ["arm64","x64"] if opts.arch == "both" else [opts.arch]:
        publish(arch, opts.model, opts.out, quest_apk)
    summary = opts.out / "SHA256SUMS.txt"
    hashes = [f"{check(path)}  {path.name}\n" for path in sorted(opts.out.glob("LivingPartner-Mac-*.zip"))]
    summary.with_suffix(".new").write_text("".join(hashes), encoding="ascii")
    summary.with_suffix(".new").replace(summary)
