<#
    Genera el paquete distribuible del cliente de escritorio.

    Uso:  pwsh tools/publicar.ps1

    Pasos: pruebas -> publicacion autocontenida -> instalador.

    Las pruebas van primero y detienen el proceso si fallan. Publicar una
    version que no pasa su propia bateria es la forma mas rapida de repartir
    un fallo conocido.
#>
[CmdletBinding()]
param(
    [switch]$SaltarPruebas
)

$ErrorActionPreference = 'Stop'

$raiz = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $raiz 'src\AssetFlow.Desktop\AssetFlow.Desktop.csproj'
$publicado = Join-Path $raiz 'src\AssetFlow.Desktop\bin\publish\win-x64'
$guion = Join-Path $raiz 'installer\AssetFlow.iss'

function Paso([string]$texto) {
    Write-Host ''
    Write-Host "==> $texto" -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
if (-not $SaltarPruebas) {
    Paso 'Ejecutando las pruebas'
    dotnet test (Join-Path $raiz 'AssetFlow.slnx') --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw 'Las pruebas han fallado. No se publica.'
    }
}

# ---------------------------------------------------------------------------
Paso 'Publicando la aplicacion (autocontenida, win-x64, sin recorte)'

if (Test-Path $publicado) {
    Remove-Item $publicado -Recurse -Force
}

dotnet publish $proyecto -p:PublishProfile=win-x64 --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Ha fallado la publicacion.'
}

$exe = Join-Path $publicado 'AssetFlow.exe'
if (-not (Test-Path $exe)) {
    throw "No se ha generado $exe"
}

$tamano = [math]::Round(((Get-ChildItem $publicado -Recurse -File |
    Measure-Object -Property Length -Sum).Sum / 1MB), 1)

Write-Host "    Carpeta publicada: $publicado"
Write-Host "    Tamano: $tamano MB"

# ---------------------------------------------------------------------------
Paso 'Compilando el instalador'

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning @'
No se ha encontrado Inno Setup 6 (ISCC.exe).

La aplicacion ya esta publicada y es utilizable tal cual desde la carpeta
indicada arriba. Para generar ademas el instalador, instala Inno Setup 6
desde https://jrsoftware.org/isdl.php y vuelve a ejecutar este guion.
'@
    exit 0
}

& $iscc $guion
if ($LASTEXITCODE -ne 0) {
    throw 'Ha fallado la compilacion del instalador.'
}

$salida = Join-Path $raiz 'installer\Output'
Get-ChildItem $salida -Filter *.exe | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 1)
    Write-Host ''
    Write-Host "    Instalador: $($_.FullName) ($mb MB)" -ForegroundColor Green
}
