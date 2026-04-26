param(
    [string]$BackendRoot = "apps/backend-api",
    [string]$Database = "VinhKhanhFoodAdmin",
    [string]$Server,
    [string]$Username,
    [string]$Password,
    [string]$BackendBaseUrl,
    [string]$ReportPath = ".artifacts/audio-audit-report.json",
    [switch]$DeleteDuplicates,
    [switch]$SyncCurrentFromBackend,
    [switch]$AlignLocalFallbackToDbPath
)

$ErrorActionPreference = 'Stop'

function Resolve-AbsolutePath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

function Parse-ConnectionString {
    param([string]$ConnectionString)

    $values = @{}
    foreach ($part in ($ConnectionString -split ';')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part.IndexOf('=') -lt 0) {
            continue
        }

        $pieces = $part.Split('=', 2)
        $values[$pieces[0].Trim()] = $pieces[1].Trim()
    }

    return $values
}

function Get-CanonicalDownloadUrl {
    param(
        [string]$AudioUrl,
        [string]$BaseUrl
    )

    if ([string]::IsNullOrWhiteSpace($AudioUrl)) {
        return $null
    }

    $absoluteUri = $null
    if ([System.Uri]::TryCreate($AudioUrl, [System.UriKind]::Absolute, [ref]$absoluteUri)) {
        return $absoluteUri.AbsoluteUri
    }

    if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
        return $null
    }

    $baseUri = [System.Uri]::new(($BaseUrl.TrimEnd('/') + '/'))
    return ([System.Uri]::new($baseUri, $AudioUrl.TrimStart('/'))).AbsoluteUri
}

function Get-AudioGroupFromFile {
    param(
        [string]$AudioRoot,
        [System.IO.FileInfo]$FileInfo
    )

    $relativePath = $FileInfo.FullName.Substring($AudioRoot.Length).TrimStart('\', '/')
    $segments = $relativePath -split '[\\/]'
    if ($segments.Length -lt 3) {
        return $null
    }

    return @{
        PoiId = $segments[0]
        LanguageCode = $segments[1]
        RelativePath = $relativePath.Replace('\', '/')
    }
}

function Get-DbAudioGuideScore {
    param([object]$Row)

    $score = 0
    $generationStatus = ''
    if ($null -ne $Row.GenerationStatus) {
        $generationStatus = $Row.GenerationStatus.ToString().Trim().ToLowerInvariant()
    }

    $publicStatus = ''
    if ($null -ne $Row.Status) {
        $publicStatus = $Row.Status.ToString().Trim().ToLowerInvariant()
    }

    $isReady =
        -not $Row.IsOutdated -and
        $generationStatus -eq 'success' -and
        $publicStatus -eq 'ready' -and
        (-not [string]::IsNullOrWhiteSpace($Row.AudioUrl) -or -not [string]::IsNullOrWhiteSpace($Row.AudioFilePath))

    if ($isReady) {
        $score += 1000
    }

    if ($generationStatus -eq 'success') {
        $score += 250
    }

    if ($publicStatus -eq 'ready') {
        $score += 150
    }

    if (-not $Row.IsOutdated) {
        $score += 120
    }

    if (-not [string]::IsNullOrWhiteSpace($Row.AudioFilePath)) {
        $score += 80
    }

    if (-not [string]::IsNullOrWhiteSpace($Row.AudioUrl)) {
        $score += 40
    }

    if (-not [string]::IsNullOrWhiteSpace($Row.ContentVersion)) {
        $score += 20
    }

    return $score
}

function Select-CanonicalDbRow {
    param([object[]]$Rows)

    if ($null -eq $Rows) {
        return $null
    }

    return @($Rows |
        Sort-Object `
            @{ Expression = { Get-DbAudioGuideScore $_ }; Descending = $true }, `
            @{ Expression = { $_.UpdatedAt }; Descending = $true }, `
            @{ Expression = { $_.Id }; Descending = $true } |
        Select-Object -First 1)[0]
}

$backendRootPath = Resolve-AbsolutePath $BackendRoot
$appSettingsPath = Join-Path $backendRootPath "appsettings.json"
$wwwrootPath = Join-Path $backendRootPath "wwwroot"
$audioRoot = Join-Path $wwwrootPath "storage/audio/pois"
$reportAbsolutePath = Resolve-AbsolutePath $ReportPath
$reportDirectory = Split-Path -Parent $reportAbsolutePath

if (-not (Test-Path -LiteralPath $appSettingsPath)) {
    throw "Khong tim thay appsettings.json tai '$appSettingsPath'."
}

if (-not (Test-Path -LiteralPath $audioRoot)) {
    throw "Khong tim thay thu muc audio POI tai '$audioRoot'."
}

if (-not (Test-Path -LiteralPath $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory | Out-Null
}

$appSettings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
$connectionSettings = Parse-ConnectionString $appSettings.ConnectionStrings.AdminSqlServer

if ([string]::IsNullOrWhiteSpace($Server)) {
    $Server = $connectionSettings["Server"]
}

if ([string]::IsNullOrWhiteSpace($Username)) {
    $Username = $connectionSettings["User ID"]
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    $Password = $connectionSettings["Password"]
}

if ([string]::IsNullOrWhiteSpace($BackendBaseUrl)) {
    $BackendBaseUrl = $appSettings.MobileDistribution.PublicBaseUrl
}

if ([string]::IsNullOrWhiteSpace($Server) -or
    [string]::IsNullOrWhiteSpace($Username) -or
    [string]::IsNullOrWhiteSpace($Password)) {
    throw "Thieu thong tin ket noi SQL Server. Hay truyen -Server, -Username, -Password hoac bo sung vao appsettings.json."
}

$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$query = @"
SET NOCOUNT ON;
SELECT
    Id,
    EntityId,
    LanguageCode,
    AudioUrl,
    AudioFilePath,
    ContentVersion,
    GenerationStatus,
    IsOutdated,
    [Status],
    UpdatedAt
FROM dbo.AudioGuides
WHERE EntityType = N'poi'
ORDER BY EntityId, LanguageCode, UpdatedAt DESC;
"@

$dbLines = & $sqlcmd `
    -S $Server `
    -d $Database `
    -U $Username `
    -P $Password `
    -W `
    -w 65535 `
    -s "|" `
    -h -1 `
    -Q $query

$dbRows = @()
foreach ($line in $dbLines) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $parts = $line.Split('|')
    if ($parts.Length -lt 10) {
        continue
    }

    $dbRows += [pscustomobject]@{
        Id = $parts[0].Trim()
        EntityId = $parts[1].Trim()
        LanguageCode = $parts[2].Trim()
        AudioUrl = $parts[3].Trim()
        AudioFilePath = $parts[4].Trim()
        ContentVersion = $parts[5].Trim()
        GenerationStatus = $parts[6].Trim()
        IsOutdated = ($parts[7].Trim() -eq "1")
        Status = $parts[8].Trim()
        UpdatedAt = [datetimeoffset]::Parse($parts[9].Trim())
    }
}

$localFiles = Get-ChildItem -LiteralPath $audioRoot -Recurse -File
$localFilesByGroup = @{}
foreach ($file in $localFiles) {
    $groupInfo = Get-AudioGroupFromFile -AudioRoot $audioRoot -FileInfo $file
    if ($null -eq $groupInfo) {
        continue
    }

    $key = "$($groupInfo.PoiId)|$($groupInfo.LanguageCode)"
    if (-not $localFilesByGroup.ContainsKey($key)) {
        $localFilesByGroup[$key] = [System.Collections.Generic.List[object]]::new()
    }

    $localFilesByGroup[$key].Add([pscustomobject]@{
        PoiId = $groupInfo.PoiId
        LanguageCode = $groupInfo.LanguageCode
        RelativePath = $groupInfo.RelativePath
        FullPath = $file.FullName
        Name = $file.Name
        Length = $file.Length
        LastWriteTimeUtc = $file.LastWriteTimeUtc
    })
}

$dbRowsByGroup = @{}
foreach ($row in $dbRows) {
    $key = "$($row.EntityId)|$($row.LanguageCode)"
    if (-not $dbRowsByGroup.ContainsKey($key)) {
        $dbRowsByGroup[$key] = [System.Collections.Generic.List[object]]::new()
    }

    $dbRowsByGroup[$key].Add($row)
}

$allGroupKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($key in $localFilesByGroup.Keys) {
    $null = $allGroupKeys.Add($key)
}
foreach ($key in $dbRowsByGroup.Keys) {
    $null = $allGroupKeys.Add($key)
}

$groups = @()
$deletedFiles = [System.Collections.Generic.List[string]]::new()
$downloadedFiles = [System.Collections.Generic.List[string]]::new()
$alignedFiles = [System.Collections.Generic.List[object]]::new()
$downloadErrors = [System.Collections.Generic.List[object]]::new()

foreach ($groupKey in $allGroupKeys | Sort-Object) {
    $separatorIndex = $groupKey.IndexOf('|')
    $poiId = $groupKey.Substring(0, $separatorIndex)
    $languageCode = $groupKey.Substring($separatorIndex + 1)
    $dbGroupRows = if ($dbRowsByGroup.ContainsKey($groupKey)) { $dbRowsByGroup[$groupKey] } else { [System.Collections.Generic.List[object]]::new() }
    $dbRow = Select-CanonicalDbRow $dbGroupRows
    $localGroup = if ($localFilesByGroup.ContainsKey($groupKey)) { $localFilesByGroup[$groupKey] } else { [System.Collections.Generic.List[object]]::new() }
    $orderedLocalFiles = @($localGroup | Sort-Object `
        @{ Expression = { $_.LastWriteTimeUtc }; Descending = $true }, `
        @{ Expression = { $_.Length }; Descending = $true }, `
        @{ Expression = { $_.Name }; Descending = $true })

    $canonicalPath = $null
    $canonicalSource = "none"
    $dbExpectedPath = $null
    $canonicalDownloadUrl = $null
    $dbFileExistsLocally = $false

    if ($null -ne $dbRow -and -not [string]::IsNullOrWhiteSpace($dbRow.AudioFilePath)) {
        $dbExpectedPath = Join-Path $wwwrootPath ($dbRow.AudioFilePath.Replace('/', '\'))
        $dbFileExistsLocally = Test-Path -LiteralPath $dbExpectedPath
        if ($dbFileExistsLocally) {
            $canonicalPath = $dbExpectedPath
            $canonicalSource = "db-local"
        }
        elseif ($SyncCurrentFromBackend -and -not [string]::IsNullOrWhiteSpace($BackendBaseUrl)) {
            $canonicalDownloadUrl = Get-CanonicalDownloadUrl -AudioUrl $dbRow.AudioUrl -BaseUrl $BackendBaseUrl
            if (-not [string]::IsNullOrWhiteSpace($canonicalDownloadUrl)) {
                $targetDirectory = Split-Path -Parent $dbExpectedPath
                if (-not (Test-Path -LiteralPath $targetDirectory)) {
                    New-Item -ItemType Directory -Path $targetDirectory | Out-Null
                }

                try {
                    Invoke-WebRequest -UseBasicParsing -Uri $canonicalDownloadUrl -OutFile $dbExpectedPath
                    $canonicalPath = $dbExpectedPath
                    $canonicalSource = "db-downloaded"
                    $downloadedFiles.Add($dbExpectedPath)
                }
                catch {
                    $downloadErrors.Add([pscustomobject]@{
                        PoiId = $poiId
                        LanguageCode = $languageCode
                        AudioGuideId = $dbRow.Id
                        DownloadUrl = $canonicalDownloadUrl
                        Error = $_.Exception.Message
                    })
                }
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($canonicalPath) -and $orderedLocalFiles.Count -gt 0) {
        $canonicalPath = $orderedLocalFiles[0].FullPath
        $canonicalSource = "local-fallback"
    }

    if ($AlignLocalFallbackToDbPath -and
        $null -ne $dbRow -and
        -not [string]::IsNullOrWhiteSpace($dbExpectedPath) -and
        -not (Test-Path -LiteralPath $dbExpectedPath) -and
        -not [string]::IsNullOrWhiteSpace($canonicalPath) -and
        [string]::Equals($canonicalSource, "local-fallback", [System.StringComparison]::OrdinalIgnoreCase)) {
        $resolvedExpectedPath = [System.IO.Path]::GetFullPath($dbExpectedPath)
        $resolvedCanonicalPath = [System.IO.Path]::GetFullPath($canonicalPath)
        $resolvedAudioRoot = [System.IO.Path]::GetFullPath($audioRoot)
        if (-not $resolvedExpectedPath.StartsWith($resolvedAudioRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not $resolvedCanonicalPath.StartsWith($resolvedAudioRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to align audio outside audio root. source='$resolvedCanonicalPath'; target='$resolvedExpectedPath'; audioRoot='$resolvedAudioRoot'."
        }

        $targetDirectory = Split-Path -Parent $resolvedExpectedPath
        if (-not (Test-Path -LiteralPath $targetDirectory)) {
            New-Item -ItemType Directory -Path $targetDirectory | Out-Null
        }

        Move-Item -LiteralPath $resolvedCanonicalPath -Destination $resolvedExpectedPath -Force
        $alignedFiles.Add([pscustomobject]@{
            PoiId = $poiId
            LanguageCode = $languageCode
            SourcePath = $resolvedCanonicalPath
            TargetPath = $resolvedExpectedPath
            DbAudioGuideId = $dbRow.Id
        })

        $canonicalPath = $resolvedExpectedPath
        $canonicalSource = "db-aligned-local"
        $dbFileExistsLocally = $true
        $localGroup = [System.Collections.Generic.List[object]]::new()
        foreach ($file in Get-ChildItem -LiteralPath (Split-Path -Parent $resolvedExpectedPath) -File) {
            $groupInfo = Get-AudioGroupFromFile -AudioRoot $audioRoot -FileInfo $file
            if ($null -ne $groupInfo -and
                [string]::Equals($groupInfo.PoiId, $poiId, [System.StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals($groupInfo.LanguageCode, $languageCode, [System.StringComparison]::OrdinalIgnoreCase)) {
                $localGroup.Add([pscustomobject]@{
                    PoiId = $groupInfo.PoiId
                    LanguageCode = $groupInfo.LanguageCode
                    RelativePath = $groupInfo.RelativePath
                    FullPath = $file.FullName
                    Name = $file.Name
                    Length = $file.Length
                    LastWriteTimeUtc = $file.LastWriteTimeUtc
                })
            }
        }

        $orderedLocalFiles = @($localGroup | Sort-Object `
            @{ Expression = { $_.LastWriteTimeUtc }; Descending = $true }, `
            @{ Expression = { $_.Length }; Descending = $true }, `
            @{ Expression = { $_.Name }; Descending = $true })
    }

    $duplicateFiles = @()
    foreach ($localFile in $orderedLocalFiles) {
        if ([string]::IsNullOrWhiteSpace($canonicalPath) -or
            -not [string]::Equals($localFile.FullPath, $canonicalPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $duplicateFiles += $localFile
        }
    }

    if ($DeleteDuplicates) {
        foreach ($duplicateFile in $duplicateFiles) {
            Remove-Item -LiteralPath $duplicateFile.FullPath -Force
            $deletedFiles.Add($duplicateFile.FullPath)
        }
    }

    $groups += [pscustomobject]@{
        PoiId = $poiId
        LanguageCode = $languageCode
        HasDbReference = ($null -ne $dbRow)
        DbRowCount = @($dbGroupRows).Count
        DbAudioGuideId = if ($null -ne $dbRow) { $dbRow.Id } else { $null }
        DbAudioFilePath = if ($null -ne $dbRow) { $dbRow.AudioFilePath } else { $null }
        DbAudioUrl = if ($null -ne $dbRow) { $dbRow.AudioUrl } else { $null }
        DbUpdatedAt = if ($null -ne $dbRow) { $dbRow.UpdatedAt } else { $null }
        DbGenerationStatus = if ($null -ne $dbRow) { $dbRow.GenerationStatus } else { $null }
        DbStatus = if ($null -ne $dbRow) { $dbRow.Status } else { $null }
        DbIsOutdated = if ($null -ne $dbRow) { $dbRow.IsOutdated } else { $null }
        DbFileExistsLocally = $dbFileExistsLocally
        DbExpectedPath = $dbExpectedPath
        CanonicalDownloadUrl = $canonicalDownloadUrl
        CanonicalPath = $canonicalPath
        CanonicalSource = $canonicalSource
        DownloadError = @($downloadErrors | Where-Object {
            $_.PoiId -eq $poiId -and $_.LanguageCode -eq $languageCode
        } | Select-Object -First 1)
        LocalFileCount = $orderedLocalFiles.Count
        DuplicateFileCount = $duplicateFiles.Count
        LocalFiles = @($orderedLocalFiles | Select-Object Name, RelativePath, Length, LastWriteTimeUtc)
        DuplicateFiles = @($duplicateFiles | Select-Object Name, RelativePath, Length, LastWriteTimeUtc)
        DbRows = @($dbGroupRows | Sort-Object UpdatedAt -Descending | Select-Object Id, AudioFilePath, AudioUrl, ContentVersion, GenerationStatus, IsOutdated, Status, UpdatedAt)
    }
}

$report = [pscustomobject]@{
    GeneratedAtUtc = [datetimeoffset]::UtcNow.ToString("O")
    BackendRoot = $backendRootPath
    AudioRoot = $audioRoot
    Server = $Server
    Database = $Database
    BackendBaseUrl = $BackendBaseUrl
    DeleteDuplicates = [bool]$DeleteDuplicates
    SyncCurrentFromBackend = [bool]$SyncCurrentFromBackend
    AlignLocalFallbackToDbPath = [bool]$AlignLocalFallbackToDbPath
    TotalDbRows = $dbRows.Count
    TotalLocalFilesBeforeCleanup = $localFiles.Count
    DeletedFileCount = $deletedFiles.Count
    DownloadedFileCount = $downloadedFiles.Count
    AlignedFileCount = $alignedFiles.Count
    AlignedFiles = $alignedFiles
    DownloadErrorCount = $downloadErrors.Count
    DownloadErrors = $downloadErrors
    Groups = $groups
}

$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportAbsolutePath -Encoding UTF8

Write-Output "Audio audit completed."
Write-Output "Report: $reportAbsolutePath"
Write-Output "Groups: $($groups.Count)"
Write-Output "DB rows: $($dbRows.Count)"
Write-Output "Deleted duplicates: $($deletedFiles.Count)"
Write-Output "Downloaded canonical files: $($downloadedFiles.Count)"
Write-Output "Aligned local fallback files: $($alignedFiles.Count)"
Write-Output "Download errors: $($downloadErrors.Count)"
