# NuGet 包處理腳本配置變數
$nugetApiKey = "ff0a2c514effdb9dab489e32334c103b11e8d4c2"  # NuGet 伺服器的 API 金鑰
$nugetServer = "EywaSystem"  # NuGet 伺服器名稱或 URL
$initialVersion = "9.0.0"  # 初始版本號
$authorsValue = "Reformtek"  # 套件作者
$descriptionValue = "for EywaSystem"  # 套件描述
# -------------------以下無須更改---------------------
$nugetFolderName = "NugetPackage"  # 定義 NuGet 包存儲的資料夾名稱
$nugetPath = Join-Path -Path $PSScriptRoot -ChildPath $nugetFolderName  # 建立 NuGet 包資料夾的完整路徑
$generatePackageOnBuildValue = "true"  # 是否在建置時產生 NuGet 包

$global:totalSteps = 0
$global:currentStep = 0

function Update-Progress {
    param ([string]$Activity)
    $global:currentStep++
    if ($global:totalSteps -le 0) {
        $percentComplete = 0
    } else {
        $percentComplete = [math]::Round(($global:currentStep / $global:totalSteps) * 100)
        if ($percentComplete -gt 100) { $percentComplete = 100 }
    }
    Write-Progress -Activity "NuGet 包處理進度" -Status "$Activity" -PercentComplete $percentComplete
}

function Write-StepOutput {
    param ([string]$Message)
    Write-Host "[$($global:currentStep)/$($global:totalSteps)] $Message" -ForegroundColor Cyan
}

function Remove-AllNugetFiles {
    if (Test-Path -Path $nugetPath) {
        Remove-Item -Path $nugetPath -Recurse -Force
    }
    Update-Progress -Activity "刪除 NuGet 資料夾"
    $nupkgFiles = Get-ChildItem -Path $PSScriptRoot -Filter "*.nupkg" -Recurse
    foreach ($file in $nupkgFiles) {
        Remove-Item -Path $file.FullName -Force -ErrorAction SilentlyContinue
    }
    Update-Progress -Activity "刪除 .nupkg 檔案"
}

function Create-NugetDirectory {
    if (Test-Path -Path $nugetPath) {
        Remove-Item -Path $nugetPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $nugetPath -Force | Out-Null
    Update-Progress -Activity "建立 NuGet 目錄"
    Write-StepOutput "已建立名為 $nugetFolderName 的資料夾。"
}

function Copy-NugetPackages {
    $nupkgFiles = Get-ChildItem -Path $PSScriptRoot -Filter "*.nupkg" -Recurse
    foreach ($nupkgFile in $nupkgFiles) {
        $destinationPath = Join-Path -Path $nugetPath -ChildPath $nupkgFile.Name
        if (-not (Test-Path -Path $nugetPath)) {
            New-Item -ItemType Directory -Path $nugetPath -Force | Out-Null
        }
        if (-not (Test-Path -Path $destinationPath)) {
            Copy-Item -Path $nupkgFile.FullName -Destination $destinationPath -Force
        }
    }
    Update-Progress -Activity "複製 NuGet 包"
}

function Build-And-Copy {
    $allDirectories = Get-ChildItem -Directory -Recurse | Where-Object { $_.FullName -notlike "*\$nugetFolderName*" }
    foreach ($directory in $allDirectories) {
        Set-Location -Path $directory.FullName
        try {
            if (Test-Path "*.csproj") {
                $buildProcess = Start-Process -FilePath "dotnet" -ArgumentList "build" -PassThru -WindowStyle Hidden
                $buildProcess.WaitForExit()
            }
        } catch {
            Write-StepOutput "執行 dotnet build 時發生錯誤：$($_.Exception.Message)"
        }
        Update-Progress -Activity "構建專案"
    }
    Set-Location -Path $PSScriptRoot
    Copy-NugetPackages
}

function Upload-NugetPackages {
    $nugetNupkgFiles = Get-ChildItem -Path $nugetPath -Filter "*.nupkg"
    Write-StepOutput "開始上傳套件到 $nugetServer NuGet 伺服器..."
    
    $successfulUploads = @()
    $failedUploads = @()
    
    Set-Location -Path $nugetPath
    foreach ($nugetNupkgFile in $nugetNupkgFiles) {
        $nugetNupkgFileName = $nugetNupkgFile.Name
        try {
            $output = dotnet nuget push -s $nugetServer -k $nugetApiKey "$nugetNupkgFileName" --skip-duplicate 2>&1
            if ($LASTEXITCODE -eq 0) {
                $successfulUploads += $nugetNupkgFileName
            } else {
                $failedUploads += @{Name = $nugetNupkgFileName; Error = $output}
            }
        } catch {
            $failedUploads += @{Name = $nugetNupkgFileName; Error = $_.Exception.Message}
        }
        Update-Progress -Activity "上傳 NuGet 包"
    }
    Set-Location -Path $PSScriptRoot

    Write-StepOutput "上傳完成。結果如下："
    if ($successfulUploads.Count -gt 0) {
        Write-Host "成功上傳的套件：" -ForegroundColor Green
        $successfulUploads | ForEach-Object { Write-Host "- $_" -ForegroundColor Green }
    }
    if ($failedUploads.Count -gt 0) {
        Write-Host "上傳失敗的套件：" -ForegroundColor Red
        $failedUploads | ForEach-Object { 
            Write-Host "- $($_.Name)" -ForegroundColor Red
            Write-Host "  錯誤：$($_.Error)" -ForegroundColor Red
        }
    }
}
function Update-Version {
    $csprojFiles = Get-ChildItem -Path $PSScriptRoot -Filter "*.csproj" -Recurse
    $initialVersionParts = $initialVersion.Split('.')

    foreach ($csprojFile in $csprojFiles) {
        $content = Get-Content -Path $csprojFile.FullName -Raw
        $updated = $false

        # 尋找第一個 <PropertyGroup>
        if ($content -match '(<PropertyGroup[^>]*>)') {
            $firstPropertyGroup = $matches[1]
        } else {
            # 如果沒有 <PropertyGroup>，在檔案開頭新增一個
            $firstPropertyGroup = "<PropertyGroup>"
            $content = "$firstPropertyGroup`n</PropertyGroup>`n" + $content
        }

        # 更新或新增 <Version>
        if ($content -match '<Version>(\d+)\.(\d+)\.(\d+)</Version>') {
            $major = [int]$matches[1]
            $minor = [int]$matches[2]
            $patch = [int]$matches[3]
            $patch++  # 遞增尾數 (patch)
            $newVersion = "$major.$minor.$patch"

            $content = $content -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$newVersion</Version>"
            Write-StepOutput "在 $($csprojFile.Name) 中將版本號更新為 $newVersion"
            $updated = $true
        } else {
            $newVersion = "$($initialVersionParts[0]).$($initialVersionParts[1]).$($initialVersionParts[2])"
            $versionTag = "    <Version>$newVersion</Version>"
            $content = $content -replace [regex]::Escape($firstPropertyGroup), "$firstPropertyGroup`n$versionTag"
            Write-StepOutput "在 $($csprojFile.Name) 的第一個 <PropertyGroup> 中新增版本號標籤為 $newVersion"
            $updated = $true
        }

        # 添加/更新其他標籤到第一個 <PropertyGroup>
        $tagsToAdd = @(
            @{ Tag = "GeneratePackageOnBuild"; Value = $generatePackageOnBuildValue },
            @{ Tag = "PackageId"; Value = [System.IO.Path]::GetFileNameWithoutExtension($csprojFile.Name) },
            @{ Tag = "Authors"; Value = $authorsValue },
            @{ Tag = "Description"; Value = $descriptionValue }
        )

        foreach ($tagData in $tagsToAdd) {
            $tag = $tagData.Tag
            $value = $tagData.Value

            if ($content -notmatch "<$tag>.*?</$tag>") {
                $tagLine = "    <$tag>$value</$tag>"
                $content = $content -replace [regex]::Escape($firstPropertyGroup), "$firstPropertyGroup`n$tagLine"
                Write-StepOutput "新增 <$tag> 標籤至 $($csprojFile.Name)"
                $updated = $true
            }
        }

        # 保存更新內容
        if ($updated) {
            Set-Content -Path $csprojFile.FullName -Value $content
            Update-Progress -Activity "更新版本號與標籤"
        }
    }
}



$global:totalSteps = (Get-ChildItem -Path $PSScriptRoot -Filter "*.csproj" -Recurse).Count * 2 + 7

function Main {
    Write-Host "開始處理 NuGet 包..." -ForegroundColor Yellow
    Remove-AllNugetFiles
    Create-NugetDirectory
    Update-Version
    Build-And-Copy
    Upload-NugetPackages
    Remove-AllNugetFiles
    Write-Host "所有步驟已完成！" -ForegroundColor Green
}

Main