"""Package existing RID publish directories without modifying user data.

Run after dotnet publish for win-x64, osx-arm64 and linux-x64.
Requires Python 3.11+. Outputs a fresh directory; never overwrites a release.
"""
import argparse
import hashlib
import json
import plistlib
import shutil
import stat
import tarfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--version', default='0.7.0')
parser.add_argument('--output', required=True)
parser.add_argument('--source-root', type=Path, default=ROOT, help='parent of the three RID publish directories')
args = parser.parse_args()
if not all(x.isdigit() for x in args.version.split('.')) or len(args.version.split('.')) != 3:
    parser.error('version must have three numeric components')
out = Path(args.output).resolve()
if out.exists():
    parser.error('output must be a new directory')
source_root = args.source_root.resolve()
sources = {'win-x64': source_root / 'publish-win-x64', 'osx-arm64': source_root / 'publish-mac-arm64', 'linux-x64': source_root / 'publish-linux-x64'}
for rid, source in sources.items():
    exe = 'PracticalTools.Avalonia' + ('.exe' if rid == 'win-x64' else '')
    if not (source / exe).is_file():
        parser.error(f'missing {source / exe}; publish first')
out.mkdir(parents=True)

def copy_publish(source, target):
    shutil.copytree(source, target, ignore=shutil.ignore_patterns('*.pdb', 'stage7-test-root.txt'))

def zip_tree(folder, archive):
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for path in sorted(folder.rglob('*')):
            if not path.is_file():
                continue
            entry = zipfile.ZipInfo(path.relative_to(folder.parent).as_posix())
            entry.create_system = 3
            mode = 0o755 if path.name == 'PracticalTools.Avalonia' or path.suffix == '.dylib' else 0o644
            entry.external_attr = (stat.S_IFREG | mode) << 16
            entry.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(entry, path.read_bytes())

windows = out / 'PracticalTools-win-x64'
copy_publish(sources['win-x64'], windows)
zip_tree(windows, out / f'PracticalTools-{args.version}-win-x64.zip')
app = out / 'PracticalTools.app'
contents = app / 'Contents'
copy_publish(sources['osx-arm64'], contents / 'MacOS')
(contents / 'Resources').mkdir()
with (contents / 'Info.plist').open('wb') as f:
    plistlib.dump({
        'CFBundleName': 'PracticalTools', 'CFBundleDisplayName': '实用工具',
        'CFBundleIdentifier': 'org.practicaltools.desktop',
        'CFBundleExecutable': 'PracticalTools.Avalonia', 'CFBundlePackageType': 'APPL',
        'CFBundleVersion': args.version, 'CFBundleShortVersionString': args.version,
        'NSHighResolutionCapable': True,
    }, f)
(contents / 'MacOS' / 'PracticalTools.Avalonia').chmod(0o755)
zip_tree(app, out / f'PracticalTools-{args.version}-osx-arm64-unsigned.zip')
linux = out / 'PracticalTools-linux-x64'
copy_publish(sources['linux-x64'], linux)
(linux / 'PracticalTools.Avalonia').chmod(0o755)
with tarfile.open(out / f'PracticalTools-{args.version}-linux-x64.tar.gz', 'w:gz') as tar:
    def permissions(info):
        info.uid = info.gid = 0
        info.uname = info.gname = ''
        info.mode = 0o755 if info.isdir() or Path(info.name).name == 'PracticalTools.Avalonia' else 0o644
        return info
    tar.add(linux, arcname=linux.name, filter=permissions)
archives = sorted(p for p in out.iterdir() if p.is_file())
def checksum(path):
    with path.open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()
(out / 'SHA256SUMS.txt').write_text(''.join(f'{checksum(p)}  {p.name}\n' for p in archives), encoding='utf-8')
(out / 'release.json').write_text(json.dumps({'version': args.version, 'candidate': True,
    'macos_signed': False, 'macos_notarized': False,
    'linux_candidate_target': 'Ubuntu 24.04 x64 (not runtime validated)',
    'artifacts': [p.name for p in archives]}, indent=2), encoding='utf-8')
print(out)
