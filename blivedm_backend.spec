# -*- mode: python ; coding: utf-8 -*-
"""
PyInstaller spec for BLivedmNotification Python backend.

Build:
    pyinstaller blivedm_backend.spec

Or via the build script:
    .\build.ps1
"""

import os

block_cipher = None

# Project root (this spec file lives at project root)
SPEC_DIR = os.path.dirname(os.path.abspath(SPECPATH))

a = Analysis(
    ['py_overlay/main.py'],
    pathex=[SPEC_DIR],
    binaries=[],
    datas=[],
    hiddenimports=[
        # pywin32 — PyInstaller cannot auto-detect these
        'win32file',
        'win32pipe',
        'win32api',
        # blivedm vendored package
        'blivedm',
        'blivedm.handlers',
        'blivedm.utils',
        'blivedm.clients',
        'blivedm.clients.web',
        'blivedm.clients.ws_base',
        'blivedm.models',
        'blivedm.models.web',
        'blivedm.models.pb',
        # blivedm dependencies
        'aiohttp',
        'brotli',
        'pure_protobuf',
        'pure_protobuf.annotations',
        'pure_protobuf.message',
        'yarl',
        # stdlib (sometimes missed)
        'asyncio',
        'http.cookies',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        # Exclude heavy unused modules to reduce size
        'tkinter',
        'matplotlib',
        'numpy',
        'scipy',
        'PIL',
        'pytest',
        'setuptools',
        'pip',
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='blivedm_backend',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=True,              # Keep console for stdout logging (captured by C#)
    disable_windowed_runtime=True,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
