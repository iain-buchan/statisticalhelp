# Windows installer

Build from a short checkout path with the .NET 10 SDK and Windows:

```powershell
.\Build-Installer.ps1 -Version 0.1.1
```

This creates the MSI and setup EXE under `Release`. The app and headless builder
are self-contained x64 .NET applications. WiX and extensions are pinned to 7.0.1;
the build passes `AcceptEula=wix7` for this invocation. Unsigned builds need no
signing certificate. `App` and existing StatsDirect installers are not changed.

The per-user install goes into `%LOCALAPPDATA%\Programs\StatsDirectHelpEditor`,
with Start-menu and desktop shortcuts using the Help Editor's delta/book/pencil icon. It requires no administrator access. The setup EXE
includes Microsoft's signed Evergreen WebView2 bootstrapper. Existing per-user
or machine-wide WebView2 is reused; otherwise an internet connection is needed
for that bootstrapper to download the shared runtime. It remains installed when
the editor is removed. The MSI alone requires WebView2 to be installed already
and checks for it before installation; use the setup EXE for a fresh computer.

The installed application seeds a separate writable user workspace once. Upgrades
and uninstall never replace or remove that workspace, drafts, recovery files or
ChatGPT account storage. The installed seed excludes personal Flare history,
publishing destinations and generated/private data. Bundled third-party licenses
and notices are retained.

For a signed release, first commit the reviewed source, then run:

```powershell
.\Build-Installer.ps1 -Version 0.1.1 -Sign `
  -SignToolPath 'C:\path\to\Windows SDK\signtool.exe' `
  -CertificateThumbprint 'YOUR_CODE_SIGNING_CERTIFICATE_SHA1_THUMBPRINT'
```

SignTool uses the certificate in the current user's Windows certificate store.
A hardware token may ask its owner for a PIN in Windows; no password or PIN is
passed on the command line. Application assemblies, MSI, Burn engine and final
bundle are timestamped and verified. The build fails if any required signature
is missing, belongs to another certificate or is not trusted.

`installer-release.json` records the exact Git commit, authored-source inventory
hash, artifact hashes, signatures and prerequisite provenance. The installed
`build-info.json` has the version and source provenance without local machine
paths. The full relative source inventory remains in the isolated build work
folder. Build success does not upload, publish or install anything.
