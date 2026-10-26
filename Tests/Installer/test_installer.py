"""Portable installer regressions. Run: python -m unittest discover -s Tests/Installer -v"""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

PACKAGE = Path(__file__).resolve().parents[2] / 'AnimaCompatibility'
spec = importlib.util.spec_from_file_location('installer', PACKAGE / 'install.py')
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)

def item(data, name='model.bin'):
    return dict(path=name, size=len(data), sha256=hashlib.sha256(data).hexdigest(), url='https://example.invalid/'+name)

class Response(io.BytesIO):
    def __init__(self, data, status=200, headers=None):
        super().__init__(data)
        self.status = status
        self.headers = headers or {}

class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="Anima test's ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.data = b'0123456789' * 20
        self.target = self.root/'weights'/'model.bin'

    def fetch(self, data=None, status=200, headers=None):
        return lambda *a, **k: Response(self.data if data is None else data, status, headers)

    def test_fresh_download_and_verified_reuse(self):
        installer.download(item(self.data), self.target, self.fetch())
        self.assertEqual(self.target.read_bytes(), self.data)
        installer.download(item(self.data), self.target, lambda *a,**k: self.fail('verified file redownloaded'))

    def test_unknown_existing_weight_is_preserved(self):
        self.target.parent.mkdir()
        self.target.write_bytes(b'user file')
        with self.assertRaisesRegex(RuntimeError, 'left untouched'):
            installer.download(item(self.data), self.target, self.fetch())
        self.assertEqual(self.target.read_bytes(), b'user file')

    def test_bad_hash_is_never_published(self):
        with self.assertRaisesRegex(RuntimeError, 'SHA-256'):
            installer.download(item(self.data), self.target, self.fetch(b'x'*len(self.data)))
        self.assertFalse(self.target.exists())
        installer.download(item(self.data), self.target, self.fetch())
        self.assertEqual(self.target.read_bytes(), self.data)

    def partial(self, data):
        self.target.parent.mkdir()
        partial = self.target.with_name(self.target.name+'.silverpine-download.part')
        partial.write_bytes(data)
        return partial

    def test_resumes_partial_with_valid_range(self):
        partial = self.partial(self.data[:35])
        def resume(request, **kwargs):
            self.assertEqual(request.get_header('Range'), 'bytes=35-')
            return Response(self.data[35:], 206, {'Content-Range':'bytes 35-199/200'})
        installer.download(item(self.data), self.target, resume)
        self.assertFalse(partial.exists())
        self.assertEqual(self.target.read_bytes(), self.data)

    def test_range_ignored_restarts_download(self):
        self.partial(self.data[:35])
        installer.download(item(self.data), self.target, self.fetch())
        self.assertEqual(self.target.read_bytes(), self.data)

    def test_wrong_range_leaves_partial_and_target_untouched(self):
        partial = self.partial(self.data[:35])
        with self.assertRaisesRegex(RuntimeError, 'range'):
            installer.download(item(self.data), self.target, self.fetch(self.data[40:], 206, {'Content-Range':'bytes 40-199/200'}))
        self.assertEqual(partial.read_bytes(), self.data[:35])
        self.assertFalse(self.target.exists())

    def test_completed_partial_is_promoted_without_network(self):
        self.partial(self.data)
        installer.download(item(self.data), self.target, lambda *a,**k: self.fail('download called'))
        self.assertEqual(self.target.read_bytes(), self.data)

    def node_fixture(self):
        package = self.root/'package'
        originals = self.root/'cache'/'upstream'
        package.mkdir(); originals.mkdir(parents=True)
        files = {'nodes.py': b'x = 1\n', '__init__.py':b'# init\n', 'requirements.txt':b'transformers\n', 'LICENSE':b'license\n', 'README.md':b'upstream\n'}
        for name,data in files.items(): (originals/name).write_bytes(data)
        (package/'nodes.py').write_bytes(b'x = 2\n')
        (package/'supported-sha256.txt').write_text(installer.digest(originals/'nodes.py', True))
        (package/'NOTICE').write_text('modified notice')
        (package/'README.md').write_text('install guide')
        return self.root/'custom_nodes'/installer.NODE_NAME, originals.parent, {'node_files':[item(data,name) for name,data in files.items()]}, package

    def test_fresh_node_contains_original_backup_notices_and_patch(self):
        node,cache,manifest,package = self.node_fixture()
        installer.install_node(node,cache,manifest,package)
        self.assertEqual((node/'nodes.py').read_bytes(), b'x = 2\n')
        self.assertEqual(next(node.glob('*.bak')).read_bytes(), b'x = 1\n')
        self.assertEqual((node/'SILVERPINE-NOTICE').read_text(), 'modified notice')
        self.assertEqual((node/'LICENSE').read_text(), 'license\n')
        installer.install_node(node,cache,manifest,package)
        self.assertEqual(len(list(node.glob('*.bak'))), 1)

    def test_known_crlf_upgrade_backs_up_exact_bytes_and_preserves_other_files(self):
        node,cache,manifest,package = self.node_fixture()
        node.mkdir(parents=True)
        (node/'nodes.py').write_bytes(b'x = 1\r\n')
        (node/'README.md').write_bytes(b'user notes')
        installer.install_node(node,cache,manifest,package)
        self.assertEqual(next(node.glob('*.bak')).read_bytes(), b'x = 1\r\n')
        self.assertEqual((node/'README.md').read_bytes(), b'user notes')

    def test_unknown_node_not_replaced(self):
        node,cache,manifest,package = self.node_fixture()
        node.mkdir(parents=True)
        (node/'nodes.py').write_bytes(b'user_changes = True\n')
        with self.assertRaisesRegex(RuntimeError, 'Unrecognized'):
            installer.install_node(node,cache,manifest,package)
        self.assertEqual((node/'nodes.py').read_bytes(), b'user_changes = True\n')
        self.assertEqual(len(list(node.iterdir())),1)

    def test_corrupt_original_download_prevents_node_installation(self):
        node,cache,manifest,package = self.node_fixture()
        (cache/'upstream'/'nodes.py').write_bytes(b'bad')
        with self.assertRaisesRegex(RuntimeError, 'upstream source'):
            installer.install_node(node,cache,manifest,package)
        self.assertFalse(node.exists())

    def test_resolves_desktop_portable_and_legacy_node_paths(self):
        root = self.root/'ComfyUI'
        custom = root/'custom_nodes'
        custom.mkdir(parents=True)
        for path in (root, custom, custom/installer.NODE_NAME, self.root):
            self.assertEqual(installer.resolve_comfy(path), root.resolve())

    def test_noninteractive_without_license_ack_does_not_write(self):
        (self.root/'custom_nodes').mkdir()
        with self.assertRaisesRegex(RuntimeError, 'Nothing installed'):
            installer.main(['--comfy-dir',str(self.root),'--non-interactive'])
        self.assertEqual([p.name for p in self.root.iterdir()], ['custom_nodes'])

    def test_linux_python_symlink_is_not_resolved_out_of_venv(self):
        python = self.root/'.venv'/'bin'/'python'
        python.parent.mkdir(parents=True)
        python.touch()
        from types import SimpleNamespace
        calls = []
        def run(args, **kwargs):
            calls.append(args)
            return SimpleNamespace(returncode=0,stderr='')
        with patch.object(installer.subprocess,'run',side_effect=run):
            installer.ensure_dependencies(self.root,None,True)
        self.assertTrue(all(c[0] == str(python) for c in calls))

if __name__ == '__main__': unittest.main()
