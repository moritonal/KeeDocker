Param(
    $KeePassSource = "C:\Users\User\Google Drive\Memory\KeePass-2.43\KeePass.exe"
)

$currentDirectory = $PSCommandPath | Split-Path -Parent

Install-Module Tfl.Powershell.Format-Task

Format-Task "Creating PLGX" {

    Write-Host "Calling $KeePassSource $currentDirectory"

    Start-Process -NoNewWindow -FilePath "$KeePassSource" -ArgumentList "--plgx-create `"$currentDirectory\KeeDocker`"" -WorkingDirectory $currentDirectory -Wait
}

$targetPath = Join-Path -Path $currentDirectory -ChildPath "dist"
$sourcePath = (Get-ChildItem -Path $currentDirectory -Filter "*.plgx").FullName;

$existingFile = Get-ChildItem -Path $targetPath -Filter "*.plgx"

if ($null -ne $existingFile) {
    Format-Task "Remove $existingFile" {
        Remove-Item -Path $existingFile
    }
}

Format-Task "Moving from $sourcePath to $targetPath" {

    Move-Item -Path $sourcePath -Destination $targetPath;
}