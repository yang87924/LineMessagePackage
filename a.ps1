# 配置變數
$packageNames = @("Eywa.LineBot")
$apiToken = "ff0a2c514effdb9dab489e32334c103b11e8d4c2"
$baseApiUrl = "http://192.168.50.4:33000/api/v1/packages/EywaSystem?q={0}&token=$apiToken"
$nugetSource = "https://gitea.reformtek.synology.me:2443/api/packages/EywaSystem/nuget/index.json"

# 結果追蹤列表
$globalSuccessfulDeletes = @{}
$globalFailedDeletes = @{}

# ANSI 轉義序列
$reset = "`e[0m"
$cyan = "`e[36m"
$red = "`e[31m"
$green = "`e[32m"
$yellow = "`e[33m"

# 進度顯示函數
function Update-Progress {
    param ([string]$Activity, [int]$Step, [int]$Total)
    $percentComplete = [math]::Round(($Step / $Total) * 100)
    Write-Progress -Activity "NuGet 包批量處理進度" -Status $Activity -PercentComplete $percentComplete
    Write-Output "$cyan[$Step/$Total] $Activity$reset"
}

# 刪除單個套件函數
function Remove-NuGetPackage {
    param (
        [string]$PackageName,
        [string]$ApiUrl,
        [string]$NugetSource,
        [string]$ApiToken
    )

    $successfulDeletes = [System.Collections.Generic.List[string]]::new()
    $failedDeletes = [System.Collections.Generic.List[PSObject]]::new()

    try {
        $response = Invoke-RestMethod -Uri $ApiUrl -Method Get -Headers @{accept = "application/json"}
        
        # 過濾出完全匹配的套件
        $matchedPackages = $response | Where-Object { $_.name -eq $PackageName }
        if ($matchedPackages.Count -eq 0) {
            Write-Host "沒有找到完全匹配的套件 '$PackageName'。" -ForegroundColor Yellow
            return $null
        }
        Write-Host "找到 $($matchedPackages.Count) 個 $PackageName 的匹配版本。" -ForegroundColor Cyan

        for ($i = 0; $i -lt $matchedPackages.Count; $i++) {
            $package = $matchedPackages[$i]
            $version = $package.version
            Update-Progress -Activity "正在處理 $PackageName 版本 $version" -Step ($i + 1) -Total $matchedPackages.Count
            
            try {
                $output = dotnet nuget delete $PackageName $version -s $NugetSource -k $ApiToken --non-interactive 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $successfulDeletes.Add($version)
                } else {
                    if ($output -like "*not found*") {
                        Write-Host "警告: 版本 $version 未找到，可能已被刪除。" -ForegroundColor Yellow
                    } else {
                        $failedDeletes.Add([PSCustomObject]@{Version = $version; Error = $output})
                    }
                }
            } catch {
                $failedDeletes.Add([PSCustomObject]@{Version = $version; Error = $_.Exception.Message})
            }
        }

        return @{
            SuccessfulDeletes = $successfulDeletes
            FailedDeletes = $failedDeletes
        }
    } catch {
        Write-Host "錯誤: 獲取 $PackageName 套件資訊失敗。錯誤信息: $_" -ForegroundColor Red
        return $null
    }
}

# 顯示結果摘要函數
function Write-BatchResultSummary {
    param (
        [hashtable]$GlobalSuccessfulDeletes,
        [hashtable]$GlobalFailedDeletes
    )

    Write-Host "`n批量刪除操作完成。結果摘要：" -ForegroundColor Yellow

    foreach ($packageName in $GlobalSuccessfulDeletes.Keys) {
        $successDeletes = $GlobalSuccessfulDeletes[$packageName]
        if ($successDeletes.Count -gt 0) {
            Write-Host "`n$packageName 成功刪除的版本：" -ForegroundColor Green
            $successDeletes | ForEach-Object { Write-Host "- $_" -ForegroundColor Green }
        }
    }

    foreach ($packageName in $GlobalFailedDeletes.Keys) {
        $failedDeletes = $GlobalFailedDeletes[$packageName]
        if ($failedDeletes.Count -gt 0) {
            Write-Host "`n$packageName 刪除失敗的版本：" -ForegroundColor Red
            $failedDeletes | ForEach-Object { 
                Write-Host "- 版本 $($_.Version)" -ForegroundColor Red
                Write-Host "  錯誤：$($_.Error)" -ForegroundColor Red
            }
        }
    }

    # 計算總體統計
    $totalSuccessPackages = ($GlobalSuccessfulDeletes.Values | Where-Object { $_.Count -gt 0 }).Count
    $totalFailedPackages = ($GlobalFailedDeletes.Values | Where-Object { $_.Count -gt 0 }).Count
    $totalSuccessVersions = ($GlobalSuccessfulDeletes.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
    $totalFailedVersions = ($GlobalFailedDeletes.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum

    Write-Host "`n總體統計：" -ForegroundColor Yellow
    Write-Host "成功刪除的套件：$totalSuccessPackages 個" -ForegroundColor Green
    Write-Host "刪除失敗的套件：$totalFailedPackages 個" -ForegroundColor Red
    Write-Host "成功刪除的版本：$totalSuccessVersions 個" -ForegroundColor Green
    Write-Host "刪除失敗的版本：$totalFailedVersions 個" -ForegroundColor Red
}

# 主批量處理邏輯
foreach ($packageName in $packageNames) {
    $apiUrl = $baseApiUrl -f $packageName
    
    Write-Host "`n開始處理套件：$packageName" -ForegroundColor Cyan
    
    $result = Remove-NuGetPackage -PackageName $packageName -ApiUrl $apiUrl -NugetSource $nugetSource -ApiToken $apiToken
    
    if ($result -ne $null) {
        $globalSuccessfulDeletes[$packageName] = $result.SuccessfulDeletes
        $globalFailedDeletes[$packageName] = $result.FailedDeletes
    }
}

# 顯示最終結果
Write-BatchResultSummary -GlobalSuccessfulDeletes $globalSuccessfulDeletes -GlobalFailedDeletes $globalFailedDeletes