"""Create Nexus player/source archives from the verified Party-Up build."""
from pathlib import Path
import hashlib
import json
import re
import zipfile
import xml.etree.ElementTree as ET

PROJECT = Path(__file__).resolve().parents[1]
GAME = PROJECT.parents[1]
OUT = PROJECT / 'Releases' / 'Nexus'
VERSION = ET.parse(PROJECT / 'PartyNode.csproj').getroot().findtext('PropertyGroup/Version')

def sha(data):
    return hashlib.sha256(data).hexdigest()

def archive(path, entries):
    with zipfile.ZipFile(path, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for name, data in sorted(entries.items()):
            info = zipfile.ZipInfo(name, (2026, 11, 2, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            output.writestr(info, data)
    with zipfile.ZipFile(path) as check:
        assert check.testzip() is None
        assert set(check.namelist()) == set(entries)
        for name, data in entries.items():
            assert check.read(name) == data
            assert not Path(name).is_absolute() and '..' not in Path(name).parts

def main():
    assert VERSION == re.search(r'Version = "([^"]+)"', (PROJECT / 'PartyNodePlugin.cs').read_text()).group(1)
    dll = PROJECT / 'bin/Release/netstandard2.1/PartyNode.dll'
    installed = GAME / 'BepInEx/plugins/PartyNode/PartyNode.dll'
    checks = json.loads((PROJECT / 'Verification/runtime-tests.json').read_text())
    assert checks['passed'] and len(checks['checks']) == 114
    assert checks['saveWritesBlocked'] and not checks['savedKingdomLoaded']
    runtime = max((p for p in (PROJECT / 'Verification').glob('Runtime-*') if p.is_dir()), key=lambda p: p.stat().st_mtime)
    tested = runtime / 'BepInEx/plugins/PartyNode/PartyNode.dll'
    assert dll.read_bytes() == installed.read_bytes() == tested.read_bytes(), 'Built/installed/tested DLLs differ'
    assert 'PARTY_NODE_RUNTIME_PASS: 114 checks' in (runtime / 'diagnostic.log').read_text()
    OUT.mkdir(parents=True, exist_ok=True)
    main_entries = {'BepInEx/plugins/PartyNode/PartyNode.dll': dll.read_bytes()}
    for name in ['README.txt', 'CHANGELOG.txt']:
        main_entries['Party-Up ' + name] = (PROJECT / 'Nexus' / name).read_bytes()
    main_zip = OUT / f'KingdomInc-Party-Up-{VERSION}.zip'
    archive(main_zip, main_entries)

    # Explicit source whitelist: never traverse diagnostic runtime junctions.
    sources = list(PROJECT.glob('*.cs')) + [PROJECT / n for n in ['PartyNode.csproj', 'README.md', 'install.ps1', 'verify-isolated.ps1', 'verify-runtime.ps1']]
    sources += [PROJECT / 'Assets' / n for n in ['party-meetup-v1.png', 'party-meetup-v1-prompt.txt', 'party-meetup-empty-v2.png', 'party-meetup-empty-v2-prompt.txt', 'party-up-thumbnail-v2.png', 'party-up-thumbnail-v2-prompt.txt']]
    sources += list((PROJECT / 'Nexus').glob('*')) + [Path(__file__).resolve()]
    source_entries = {'_Mods/PartyNode/' + path.relative_to(PROJECT).as_posix(): path.read_bytes() for path in sources if path.is_file()}
    source_entries['DEVELOPER README.txt'] = (
        f'Party-Up {VERSION} developer source for Kingdom .Inc.\n'
        'Not a player installation archive. Extract into a development game root.\n'
        'Requires .NET SDK and local KingdomInc_Data/Managed plus BepInEx/core references.\n'
        'Build: dotnet build _Mods/PartyNode/PartyNode.csproj -c Release\n'
        'Diagnostic: run _Mods/PartyNode/verify-isolated.ps1; no user kingdom is loaded.\n'
        'Packaging additionally requires Python and a matching installed/tested DLL.\n'
        'Game assemblies, loader binaries, saves, local configs and diagnostic output are excluded.\n'
    ).encode()
    source_zip = OUT / f'KingdomInc-Party-Up-{VERSION}-source.zip'
    archive(source_zip, source_entries)
    for name in ['NEXUS_DESCRIPTION.bbcode.txt', 'UPLOAD_NOTES.md', 'CHANGELOG.txt']:
        (OUT / name).write_bytes((PROJECT / 'Nexus' / name).read_bytes())
    (OUT / 'party-up-thumbnail-v2.png').write_bytes((PROJECT / 'Assets/party-up-thumbnail-v2.png').read_bytes())
    screenshots = OUT / 'Screenshots'; screenshots.mkdir(exist_ok=True)
    for name in ['party-node-upgraded.png', 'party-settings.png', 'party-auto-send-research.png', 'party-destination-blocked.png']:
        (screenshots / name).write_bytes((PROJECT / 'Verification' / name).read_bytes())
    digests = {path.name: sha(path.read_bytes()) for path in [main_zip, source_zip, OUT / 'party-up-thumbnail-v2.png']}
    (OUT / 'SHA256.json').write_text(json.dumps(digests, indent=2) + '\n')
    report = {'name': 'Party-Up', 'game': 'Kingdom .Inc', 'version': VERSION, 'runtimeChecksPassed': len(checks['checks']),
              'testedAtUtc': checks['testedAtUtc'], 'dllSha256': sha(dll.read_bytes()),
              'referenceGameAssemblySha256': sha((GAME / 'KingdomInc_Data/Managed/Assembly-CSharp.dll').read_bytes()),
              'mainArchiveFiles': sorted(main_entries), 'sourceArchiveFiles': len(source_entries), 'sha256': digests}
    (OUT / 'RELEASE_VERIFICATION.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))

if __name__ == '__main__':
    main()
