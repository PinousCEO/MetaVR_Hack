"""Package the exported Yonder desktop player and self-contained host with NSIS 3."""
import argparse
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def quote(value):
    return '"' + str(value).replace('$', '$$').replace('"', '$\\"') + '"'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--makensis', default='makensis.exe')
    parser.add_argument('--build', type=Path, default=ROOT / 'dist/Yonder')
    parser.add_argument('--output', type=Path, default=ROOT / 'dist/Yonder/Release/Yonder-Windows-Setup.exe')
    args = parser.parse_args()
    build = args.build.resolve()
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    files = {}
    for subtree in ('LivingAIPartner-Host', 'LivingAIPartner-Windows'):
        for file in (build / subtree).rglob('*'):
            if file.is_file() and not any('DoNotShip' in part for part in file.parts) and file.suffix.lower() not in ('.pdb', '.lib'):
                files[file.relative_to(build)] = file
    for name in ('Start-Yonder.cmd', 'Start-LivingPartner.ps1', 'Play-Demon-Gate.cmd'):
        files[Path(name)] = build / name
    for name in ('Set-OpenAiKey.ps1','Set-TripoKey.ps1'):
        files[Path('tools') / name] = ROOT / 'tools' / name
    for required in ('LivingAIPartner-Windows/LivingAIPartner.exe', 'LivingAIPartner-Host/LivingPartner.Host.exe', 'LivingAIPartner-Host/coreclr.dll'):
        if Path(required) not in files:
            raise FileNotFoundError(build / required)

    groups = {}
    for rel, file in sorted(files.items()):
        groups.setdefault(rel.parent, []).append(file)
    install = []
    uninstall = []
    for folder, items in groups.items():
        dest = '$INSTDIR' if folder == Path('.') else '$INSTDIR\\' + str(folder)
        install.append(f'SetOutPath "{dest}"')
        for file in items:
            install.append('File ' + quote(file))
            uninstall.append(f'Delete "{dest}\\{file.name}"')
    dirs = {parent for rel in files for parent in rel.parents if parent != Path('.')}
    uninstall.extend(f'RMDir "$INSTDIR\\{d}"' for d in sorted(dirs,key=lambda d:-len(d.parts)))

    template = (ROOT / 'tools/YonderSetup.nsi').read_text(encoding='utf8')
    script = template.replace('@OUTPUT@', quote(output)).replace('@INSTALL@','\n'.join(install)).replace('@UNINSTALL@','\n'.join(uninstall))
    with tempfile.TemporaryDirectory() as work:
        path = Path(work) / 'package.nsi'
        path.write_text(script, encoding='utf-8-sig')
        subprocess.run([args.makensis, '/V2', str(path)], check=True)
    print(f'{output.name} ({len(files)} payload files)')


if __name__ == '__main__':
    main()
