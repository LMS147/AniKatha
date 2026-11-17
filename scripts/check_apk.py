"""Catch an APK that compiles but still requires separate fast-deploy files."""
import sys
from zipfile import ZipFile

with ZipFile(sys.argv[1]) as apk:
    names = apk.namelist()
    assemblies = [name for name in names if name.lower().endswith(('.dll', '.dll.so')) or name.endswith('/libassembly-store.so') or ('assemblies' in name.lower() and '.blob' in name.lower())]
    assert assemblies, 'APK has no bundled .NET assemblies; enable EmbedAssembliesIntoApk.'
    assert any(name.endswith('wwwroot/index.html') for name in names), 'Blazor host page is missing.'
    assert any('x86_64/' in name for name in names), 'APK must include x86_64 for emulator startup checks.'
    assert any('arm64-v8a/' in name for name in names), 'APK must include arm64-v8a for physical phones.'
    print(f'PASS: embedded .NET assemblies ({len(assemblies)} files), Blazor host page, phone and emulator ABIs.')
