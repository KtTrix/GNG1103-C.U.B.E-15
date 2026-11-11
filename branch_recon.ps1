# branch_recon.ps1  (version 2)
# Prints a short, read-only summary of every branch on GitHub so we can plan how to combine them.
# It does NOT change any of your files or branches.
#
# Run it from your Unity project folder (the one that has the .git folder):
#   powershell -ExecutionPolicy Bypass -File .\branch_recon.ps1 | Out-File -Encoding utf8 $HOME\Desktop\branch_recon.txt
# then attach branch_recon.txt to the chat.
#
# Optional: compare against a specific branch instead of the automatic choice:
#   powershell -ExecutionPolicy Bypass -File .\branch_recon.ps1 -Base origin/ui_updates
#
# How it works: for each branch it finds the Unity project (it may be inside a sub-folder),
# then lists the files under Assets that do NOT exist at the same path on the base branch.
# Unity's default template files (Settings, TutorialInfo, SampleScene...) are set aside, so what
# is left is the branch owner's real work.

param([string]$Base = "")

$env:GIT_PAGER = "cat"
git fetch --all --prune 2>&1 | Out-Null

# Pick the branch to compare against: ours if it exists, otherwise main/master.
if ($Base -eq "") {
    foreach ($candidate in @("origin/ui_updates", "origin/ui-updates", "origin/main", "origin/master")) {
        git rev-parse --verify $candidate 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $Base = $candidate; break }
    }
}
"Comparing every branch against: $Base"
"Run on: " + (Get-Date -Format "yyyy-MM-dd HH:mm")
""

# Every path that exists on the base branch, and its scripts by file name.
$baseSet = @{}
$baseScripts = @{}
git ls-tree -r --name-only $Base | ForEach-Object {
    $baseSet[$_] = $true
    if ($_ -match "\.cs$") { $baseScripts[(Split-Path $_ -Leaf)] = $_ }
}

# Files every new Unity project contains. They are not anyone's own work.
$noise = "^Assets/(Settings|TutorialInfo)/|^Assets/(Readme|InputSystem_Actions|DefaultVolumeProfile|UniversalRenderPipelineGlobalSettings|Scenes/SampleScene)"

$branches = git for-each-ref refs/remotes/origin --format="%(refname:short)" |
    Where-Object { $_ -ne "origin" -and $_ -ne "origin/HEAD" -and $_ -ne $Base }

foreach ($b in $branches) {
    "=================================================="
    $b
    "=================================================="
    git log -1 --format="Last commit: %an, %ad - %s" --date=short $b

    $allFiles = @(git ls-tree -r --name-only $b)
    $versionFiles = @($allFiles | Where-Object { $_ -match "ProjectSettings/ProjectVersion\.txt$" })
    if ($versionFiles.Count -eq 0) { "No Unity project found in this branch."; "" ; continue }

    foreach ($vf in $versionFiles) {
        # $root is "" when the project is at the top level, otherwise "folder/"
        $root = $vf -replace "ProjectSettings/ProjectVersion\.txt$", ""
        if ($root -eq "") { "Unity project at: (top level of the repo)" } else { "Unity project at: $root" }
        "  " + (git show "${b}:${vf}" | Select-Object -First 1)
        git show "${b}:${root}Packages/manifest.json" 2>$null |
            Select-String "render-pipelines|openxr|com\.unity\.xr|inputsystem|cinemachine|timeline|shadergraph|ai\.navigation" |
            ForEach-Object { "  package: " + $_.Line.Trim() }

        # Assets of this project, as paths relative to the project (Assets/...).
        $assets = @($allFiles | Where-Object { $_.StartsWith($root + "Assets/") } | ForEach-Object { $_.Substring($root.Length) })
        $new = @($assets | Where-Object { -not $baseSet.ContainsKey($_) })
        $work = @($new | Where-Object { $_ -notmatch $noise })
        $same = @($assets | Where-Object { $baseSet.ContainsKey($_) -and $_ -notmatch $noise })

        "Files under Assets: " + $assets.Count + "   not on ${Base}: " + $new.Count +
            "   of those, their own work (not Unity template files): " + $work.Count

        if ($work.Count -gt 0) {
            "Where their work is:"
            $work | ForEach-Object {
                $parts = $_ -split "/"
                if ($parts.Count -le 1) { "(Assets)" }
                else { ($parts[0..([Math]::Min(2, $parts.Count - 2))]) -join "/" }
            } | Group-Object | Sort-Object Count -Descending | Select-Object -First 15 |
                ForEach-Object { "  {0,5}  {1}" -f $_.Count, $_.Name }

            $scenes = @($work | Where-Object { $_ -match "\.unity$" })
            if ($scenes.Count -gt 0) { "Scenes:"; $scenes | ForEach-Object { "  $_" } }

            "Counts:  scripts " + @($work | Where-Object { $_ -match "\.cs$" }).Count +
                "   prefabs " + @($work | Where-Object { $_ -match "\.prefab$" }).Count +
                "   materials " + @($work | Where-Object { $_ -match "\.mat$" }).Count +
                "   models " + @($work | Where-Object { $_ -match "\.(fbx|obj|blend|glb|gltf)$" }).Count +
                "   textures " + @($work | Where-Object { $_ -match "\.(png|jpg|jpeg|tga|exr|hdr|psd)$" }).Count +
                "   .meta files " + @($work | Where-Object { $_ -match "\.meta$" }).Count

            # Scripts with the same name as one of ours, at a different path: probable class-name clash.
            $clashes = @($work | Where-Object {
                $_ -match "\.cs$" -and $baseScripts.ContainsKey((Split-Path $_ -Leaf))
            })
            if ($clashes.Count -gt 0) {
                "Scripts with the same file name as one of ours (check for clashes):"
                $clashes | ForEach-Object { "  $_   (ours: " + $baseScripts[(Split-Path $_ -Leaf)] + ")" }
            }
        }

        if ($same.Count -gt 0) {
            "Files at the SAME path as one of ours (copying would overwrite ours):"
            $same | Select-Object -First 15 | ForEach-Object { "  $_" }
            if ($same.Count -gt 15) { "  ... and " + ($same.Count - 15) + " more" }
        }
    }

    # ---- very large files (GitHub rejects 100 MB, and big files slow everyone down) ----
    $big = git ls-tree -r -l $b | ForEach-Object {
        $parts = $_ -split "`t", 2
        $meta = $parts[0] -split "\s+"
        $size = [int64]0
        if ([int64]::TryParse($meta[3], [ref]$size) -and $size -gt 20MB) {
            "  {0,7:N1} MB  {1}" -f ($size / 1MB), $parts[1]
        }
    }
    if ($big) { "Files over 20 MB:"; $big }
    ""
}