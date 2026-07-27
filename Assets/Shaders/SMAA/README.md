# SMAA

Vendored from the reference SMAA implementation by Jorge Jimenez et al.
Upstream: https://github.com/iryoku/smaa (MIT, see `LICENSE.txt`).

| File | Origin |
| --- | --- |
| `SMAA.hlsl` | Upstream `SMAA.hlsl`, unmodified. |
| `LICENSE.txt` | Upstream `LICENSE.txt`. |
| `AreaTex.bytes` | Raw bytes extracted from upstream `Textures/AreaTex.h` (160x560, 2 channels x 8 bits = 2 B/px, 179 200 bytes). |
| `SearchTex.bytes` | Raw bytes extracted from upstream `Textures/SearchTex.h` (64x16, 1 channel x 8 bits = 1 B/px, 1 024 bytes). |
| `Smaa.shader` | Written for this mod. Unity built-in-pipeline wrapper driving SMAA's three passes. |

The lookup tables ship as raw `.bytes` rather than imported textures so that format,
filtering, wrap mode and colour space are set explicitly in `SmaaResources.cs` instead of
depending on Unity import settings.

They are uploaded as `TextureFormat.RG16` and `TextureFormat.R8` respectively. Note that
Unity's `RG16` means 16 bits *total* (two 8-bit channels, 2 B/px), not 16 bits per channel
as the same name implies in DXGI or OpenGL. Use 2 B/px when validating `AreaTex.bytes`.
Because the area table is a two-channel texture, the shader overrides SMAA's
`SMAA_AREATEX_SELECT` to `.rg`; the `SMAA_HLSL_3` default of `.ra` is for the DX9 reference
implementation, which loads it as A8L8.

The table bytes are uploaded **unmodified** (no row reversal). This was determined by
testing in-game: reversing the rows makes edge quality visibly worse. It also matches how
Unity's own Post Processing Stack ships the same tables.

## Regenerating the lookup tables

```powershell
curl.exe -o AreaTex.h   https://raw.githubusercontent.com/iryoku/smaa/master/Textures/AreaTex.h
curl.exe -o SearchTex.h https://raw.githubusercontent.com/iryoku/smaa/master/Textures/SearchTex.h

foreach ($p in @('AreaTex', 'SearchTex')) {
    $txt   = [IO.File]::ReadAllText((Resolve-Path "$p.h"))
    $body  = $txt.Substring($txt.IndexOf('{') + 1)
    $m     = [regex]::Matches($body, '0x([0-9a-fA-F]{2})')
    $bytes = New-Object byte[] $m.Count
    for ($i = 0; $i -lt $m.Count; $i++) { $bytes[$i] = [Convert]::ToByte($m[$i].Groups[1].Value, 16) }
    [IO.File]::WriteAllBytes((Join-Path (Get-Location) "$p.bytes"), $bytes)
}
```
