#!/usr/bin/env python3
"""Install the Silverpine Anima node update and reference models on Windows/macOS/Linux."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import urllib.request

PACKAGE = Path(__file__).resolve().parent
NODE_NAME = 'ComfyUI-Anima_IP-Adapter'

def digest(path, normalize=False):
    if normalize:
        return hashlib.sha256(path.read_bytes().replace(b'\r\n', b'\n')).hexdigest()
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(4 * 1024 * 1024), b''):
            result.update(chunk)
    return result.hexdigest()

def verified(path, item):
    return path.is_file() and path.stat().st_size == item['size'] and digest(path) == item['sha256']

def atomic_write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=path.name + '.silverpine-', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary): os.unlink(temporary)

def download(item, target, opener=urllib.request.urlopen):
    """Resume owned partials; never replace a different existing model file."""
    if target.exists():
        if verified(target, item):
            print('Verified existing:', target, flush=True)
            return
        raise RuntimeError('Existing file differs from the pinned original; left untouched: ' + str(target)
                           + '\nRename it to keep it before downloading the original.')
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name + '.silverpine-download.part')
    if partial.exists() and partial.stat().st_size >= item['size']:
        if verified(partial, item):
            os.replace(partial, target)
            return
        partial.unlink()
    start = partial.stat().st_size if partial.exists() else 0
    headers = {'User-Agent': 'Silverpine-Anima-Installer/1.10.0'}
    if start: headers['Range'] = 'bytes=%d-' % start
    print('Downloading:', item['path'], '(%0.1f MB)' % (item['size'] / 1e6), flush=True)
    with opener(urllib.request.Request(item['url'], headers=headers), timeout=60) as response:
        append = start > 0 and response.status == 206
        if append and not response.headers.get('Content-Range', '').startswith('bytes %d-' % start):
            raise RuntimeError('Server returned an unexpected download range.')
        total = start if append else 0
        last = time.monotonic()
        with partial.open('ab' if append else 'wb') as stream:
            while True:
                chunk = response.read(4 * 1024 * 1024)
                if not chunk: break
                total += len(chunk)
                if total > item['size']: raise RuntimeError('Download exceeded expected size: ' + item['path'])
                stream.write(chunk)
                if time.monotonic() - last >= 3:
                    print('  %0.1f%%' % (100 * total / item['size']), flush=True)
                    last = time.monotonic()
    if not verified(partial, item):
        raise RuntimeError('Download failed SHA-256 verification: ' + item['path'] + '. Re-run to retry.')
    os.replace(partial, target)
    print('Verified SHA-256:', target.name, flush=True)

def resolve_comfy(path):
    chosen = Path(path).expanduser().resolve()
    if chosen.name == NODE_NAME and chosen.parent.name == 'custom_nodes': chosen = chosen.parent.parent
    if chosen.name == 'custom_nodes': chosen = chosen.parent
    if (chosen/'ComfyUI'/'custom_nodes').is_dir(): chosen = chosen/'ComfyUI'
    if not (chosen/'custom_nodes').is_dir():
        raise RuntimeError('Choose the ComfyUI data folder containing custom_nodes and models: ' + str(chosen))
    return chosen

def choose_root(argument, non_interactive):
    if argument: return resolve_comfy(argument)
    if non_interactive: raise RuntimeError('--comfy-dir is required in non-interactive mode.')
    candidates = [Path.home()/'Documents'/'ComfyUI', Path.home()/'ComfyUI', Path.cwd()]
    found = next((p for p in candidates if (p/'custom_nodes').is_dir()), None)
    selected = input('ComfyUI data folder' + (' [%s]' % found if found else '') + ': ').strip().strip('"')
    if not selected and not found: raise RuntimeError('No ComfyUI folder selected.')
    return resolve_comfy(selected or found)

def check_existing_node(node, package=PACKAGE):
    replacement = package/'nodes.py'
    compile(replacement.read_bytes(), str(replacement), 'exec')
    current = node/'nodes.py'
    if not current.exists():
        if node.exists() and any(node.iterdir()):
            raise RuntimeError('Nonempty node folder has no nodes.py; no files replaced: ' + str(node))
        return
    allowed = set((package/'supported-sha256.txt').read_text().split())
    allowed.add(digest(replacement, normalize=True))
    if digest(current, normalize=True) not in allowed:
        raise RuntimeError('Unrecognized node version or custom edits. No node files changed.\n'
                           'Review compatibility.patch before applying manually: ' + str(current))

def install_node(node, cache, manifest, package=PACKAGE):
    check_existing_node(node, package)
    current = node/'nodes.py'
    originals = cache/'upstream'
    for item in manifest['node_files']:
        if not verified(originals/item['path'], item):
            raise RuntimeError('Verified upstream source is missing: ' + item['path'])
    if current.exists() and digest(current, True) != digest(package/'nodes.py', True):
        backup = current.with_name('nodes.py.before-silverpine-' + digest(current, True)[:12] + '.bak')
        if backup.exists() and backup.read_bytes() != current.read_bytes():
            raise RuntimeError('Backup name contains different bytes; no node files changed.')
        if not backup.exists(): atomic_write(backup, current.read_bytes())
        print('Node backup:', backup)
    elif not current.exists():
        original = originals/'nodes.py'
        atomic_write(node/('nodes.py.before-silverpine-' + digest(original, True)[:12] + '.bak'), original.read_bytes())
    for name in ('__init__.py', 'requirements.txt', 'LICENSE', 'README.md'):
        if not (node/name).exists(): atomic_write(node/name, (originals/name).read_bytes())
    atomic_write(current, (package/'nodes.py').read_bytes())
    for source, name in [('NOTICE','SILVERPINE-NOTICE'),('README.md','SILVERPINE-README.md')]:
        atomic_write(node/name, (package/source).read_bytes())
    print('Installed separate-reference node:', node)

def python_candidates(root):
    for env in (root/'.venv', root/'venv', root.parent/'.venv', root.parent/'venv'):
        yield env/'Scripts'/'python.exe'
        yield env/'bin'/'python'
        yield env/'bin'/'python3'
    yield root.parent/'python_embeded'/'python.exe'
    yield root.parent/'python_embedded'/'python.exe'

def ensure_dependencies(root, argument, non_interactive):
    python = Path(argument).expanduser().absolute() if argument else next((p for p in python_candidates(root) if p.is_file()), None)
    if python is None:
        if non_interactive:
            raise RuntimeError('Cannot locate ComfyUI Python. Supply --python, or --skip-dependencies if already installed.')
        print('Use the Python executable that launches ComfyUI, not an unrelated system environment.')
        python = Path(input('ComfyUI Python executable: ').strip().strip('"')).expanduser().absolute()
    if not python.is_file(): raise RuntimeError('ComfyUI Python does not exist: ' + str(python))
    print('Checking dependencies in:', python, flush=True)
    core = subprocess.run([str(python), '-c', 'import torch, torchvision, safetensors, PIL, numpy'], capture_output=True, text=True)
    if core.returncode:
        raise RuntimeError('This Python lacks working ComfyUI dependencies. Start/fix ComfyUI first.\n' + core.stderr[-2000:])
    probe = subprocess.run([str(python), '-c', 'import importlib.util, sys; sys.exit(0 if importlib.util.find_spec("transformers") else 3)'])
    if probe.returncode == 3:
        print('Installing tested missing dependency: transformers==4.57.3', flush=True)
        subprocess.run([str(python), '-m', 'pip', 'install', 'transformers==4.57.3'], check=True)
    elif probe.returncode: raise RuntimeError('Unable to inspect the ComfyUI Python environment.')
    siglip = subprocess.run([str(python), '-c', 'from transformers import SiglipVisionModel'], capture_output=True, text=True)
    if siglip.returncode:
        raise RuntimeError('Existing transformers cannot load SigLIP2 and was not replaced. Tested version: 4.57.3.\n' + siglip.stderr[-2000:])

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', nargs='?', help='ComfyUI data folder (also accepts the old node-folder argument)')
    parser.add_argument('--comfy-dir', help='Folder containing custom_nodes and models; accepts a portable parent')
    parser.add_argument('--python', help='ComfyUI Python executable if not found automatically')
    parser.add_argument('--node-only', action='store_true', help='Code only; skip weights and dependency changes')
    parser.add_argument('--skip-dependencies', action='store_true', help='Keep Python dependencies untouched (advanced)')
    parser.add_argument('--accept-model-license', action='store_true', help='Acknowledge displayed model terms without prompting')
    parser.add_argument('--non-interactive', action='store_true', help='Require explicit paths and acknowledgement')
    args = parser.parse_args(argv)
    manifest = json.loads((PACKAGE/'install-manifest.json').read_text())
    root = choose_root(args.comfy_dir or args.path, args.non_interactive)
    node = root/'custom_nodes'/NODE_NAME
    check_existing_node(node)
    cache = root/'user'/'silverpine-anima-installer'
    print('ComfyUI data folder:', root)
    print('Node code: Apache License 2.0. Original revision:', manifest['upstream_revision'])
    print('No game checkpoints, style LoRAs, text encoders, or VAEs are installed by this helper.')
    if not args.node_only:
        terms = manifest['weights_license']
        print('\nAdapter weights: author identifies CircleStone Non-Commercial License v1.0;')
        print('the author-linked document currently says:', terms['title'])
        print('This mismatch is documented, not resolved or relicensed by this installer.')
        print('Author statement:', terms['author_statement'])
        print('Referenced terms:', terms['url'])
        print('A copy is included in WEIGHTS-LICENSE.reference.txt. SigLIP2 uses Apache 2.0.')
        print('Review terms for your intended use. Commercial/product use may need separate permission.')
        if not args.accept_model_license:
            if args.non_interactive or input('Download official weights under their upstream terms? Type YES: ').strip() != 'YES':
                raise RuntimeError('Model download not accepted. Nothing installed. Use --node-only for code alone.')
        if not args.skip_dependencies: ensure_dependencies(root, args.python, args.non_interactive)
    for item in manifest['node_files']: download(item, cache/'upstream'/item['path'])
    if not args.node_only:
        for item in manifest['models']: download(item, root/item['path'])
        notices = root/'models'/'ipadapter'/'Silverpine-Anima-Licenses'
        for name in ('WEIGHTS-LICENSE.reference.txt','LICENSE','NOTICE'):
            atomic_write(notices/name, (PACKAGE/name).read_bytes())
        atomic_write(notices/'install-manifest.json', (PACKAGE/'install-manifest.json').read_bytes())
    install_node(node, cache, manifest)
    atomic_write(cache/'last-install.json', json.dumps({'version':manifest['version'], 'node':str(node),
        'node_sha256':digest(node/'nodes.py'), 'models_verified':not args.node_only,
        'upstream_revision':manifest['upstream_revision']}, indent=2).encode())
    print('\nComplete. Restart ComfyUI, then Connect / refresh lists in the mod.')
    print('Restart Silverpine separately to load the mod DLL. Matching models are reused.')
    return 0

if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (Exception, KeyboardInterrupt) as error:
        print('\nInstallation stopped:', error, file=sys.stderr)
        print('Completed downloads are reusable; incomplete downloads can resume. No ComfyUI process was stopped.', file=sys.stderr)
        raise SystemExit(1)
